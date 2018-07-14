﻿using System;
using System.Linq;
using System.Collections.Generic;

namespace Duality.IO
{
	/// <summary>
	/// A specialized data structure for queueing <see cref="FileEvent"/> data and
	/// normalizing it by aggregating event groups and duplicate events.
	/// </summary>
	public class FileEventQueue
	{
		private List<FileEvent> items = new List<FileEvent>();

		/// <summary>
		/// Whether there are any events in the queue.
		/// </summary>
		public bool IsEmpty
		{
			get { return this.items.Count == 0; }
		}
		/// <summary>
		/// A list of normalized events in this queue.
		/// </summary>
		public IReadOnlyList<FileEvent> Items
		{
			get { return this.items; }
		}


		/// <summary>
		/// Adds a new <see cref="FileEvent"/> at the end of the queue.
		/// </summary>
		/// <param name="fileEvent"></param>
		public void Add(FileEvent fileEvent)
		{
			this.items.Add(fileEvent);
			this.AggregateWithLatest();
		}
		/// <summary>
		/// Clears the queue of all events.
		/// </summary>
		public void Clear()
		{
			this.items.Clear();
		}
		/// <summary>
		/// Removes all events from the queue that match the specified predicate.
		/// </summary>
		/// <param name="predicate"></param>
		public void ApplyFilter(Predicate<FileEvent> predicate)
		{
			this.items.RemoveAll(predicate);
		}

		private void AggregateWithLatest()
		{
			int currentIndex = this.items.Count - 1;
			FileEvent current = this.items[currentIndex];
			string currentOldFileName = PathOp.GetFileName(current.OldPath);
			string currentFileName = PathOp.GetFileName(current.Path);

			// Aggregate with previous events, so the latest event
			// in an aggregate chain is the one that defines event order.
			for (int prevIndex = currentIndex - 1; prevIndex >= 0; prevIndex--)
			{
				FileEvent prev = this.items[prevIndex];
				string prevFileName = PathOp.GetFileName(prev.Path);

				// Aggregate identical events
				if (current.Equals(prev))
				{
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}

				// Aggregate "delete Foo/A, create Bar/A" to "rename Foo/A to Bar/A" events.
				if (current.Type == FileEventType.Created &&
					prev.Type == FileEventType.Deleted &&
					currentFileName == prevFileName)
				{
					current.Type = FileEventType.Renamed;
					current.OldPath = prev.Path;
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}

				// Aggregate sequential renames / moves of the same file
				if (current.Type == FileEventType.Renamed &&
					prev.Type == FileEventType.Renamed &&
					currentOldFileName == prevFileName)
				{
					current.OldPath = prev.OldPath;
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}

				// Aggregate "delete A, then rename B to A" into "rename B to A, changed A" events.
				// Some applications (like Photoshop) do stuff like that when saving files.
				if (current.Type == FileEventType.Renamed &&
					prev.Type == FileEventType.Deleted &&
					current.Path == prev.Path)
				{
					FileEvent rename = current;
					this.items.Insert(currentIndex, rename);
					currentIndex++;

					current.Type = FileEventType.Changed;
					current.OldPath = current.Path;
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}

				// Aggregate anything before a delete into just the delete
				if (current.Type == FileEventType.Deleted &&
					prev.Path == current.Path)
				{
					// Special case for previous renames: Translate the delete back to the old path
					if (prev.Type == FileEventType.Renamed)
					{
						current.Path = prev.OldPath;
						current.OldPath = prev.OldPath;
					}
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}

				// Aggregate anything after a create into just the create
				if (prev.Type == FileEventType.Created &&
					(prev.Path == current.Path || prev.Path == current.OldPath))
				{
					current.Type = FileEventType.Created;
					current.OldPath = current.Path;
					this.items.RemoveAt(prevIndex);
					currentIndex--;
					continue;
				}
			}

			// Filter out no-op events
			if (current.Type == FileEventType.Renamed &&
				current.OldPath == current.Path)
			{
				this.items.RemoveAt(currentIndex);
				return;
			}

			// Assign back the modified current file event after its potential aggregation
			this.items[currentIndex] = current;
		}
	}
}
