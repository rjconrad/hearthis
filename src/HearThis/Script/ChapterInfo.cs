// --------------------------------------------------------------------------------------------
#region // Copyright (c) 2014, SIL International. All Rights Reserved.
// <copyright from='2011' to='2014' company='SIL International'>
//		Copyright (c) 2014, SIL International. All Rights Reserved.
//
//		Distributable under the terms of the MIT License (http://sil.mit-license.org/)
// </copyright>
#endregion
// --------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using DesktopAnalytics;
using HearThis.Properties;
using HearThis.Publishing;
using SIL.IO;
using SIL.Xml;

namespace HearThis.Script
{
	/// ------------------------------------------------------------------------------------
	/// <summary>
	/// Class to maintain information about chapters and support XML serialization
	/// </summary>
	/// ------------------------------------------------------------------------------------
	[Serializable]
	[XmlType(AnonymousType = true)]
	[XmlRoot(Namespace = "", IsNullable = false)]
	public class ChapterInfo
	{
		public const string kChapterInfoFilename = "info.xml";

		private string _projectName;
		private string _bookName;
		private int _bookNumber;
		private IScriptProvider _scriptProvider;
		private string _filePath;

		[XmlAttribute("Number")]
		public int ChapterNumber1Based;

		/// <summary>
		/// This is for informational purposes only (at least for now). Any recording made
		/// in HearThis before this was implemented will not be reflected in this list, so
		/// it should NOT be used to determine the actual number of recordings. If/when we
		/// start to use this to determine if recordings are possibly out-of-date, we'll
		/// need to handle the case where a recording exists but is not reflected here.
		/// To enable XML serialization, this is not a SortedList, but it is expected to be
		/// ordered by LineNumber.
		/// </summary>
		public List<ScriptLine> Recordings { get; set; }

		/// <summary>
		/// This records the lines we want to record for this chapter. May be out of date compared to
		/// current ScriptProvider; this is primarily output data for HearThisAndroid.
		/// May also be missing if file was created by an older version of HearThis.
		/// </summary>
		public List<ScriptLine> Source { get; set; }

		/// <summary>
		/// Use this instead of the default constructor to instantiate an instance of this class
		/// </summary>
		/// <param name="book">Info about the book containing this chapter</param>
		/// <param name="chapterNumber1Based">[0] == intro, [1] == chapter 1, etc.</param>
		public static ChapterInfo Create(BookInfo book, int chapterNumber1Based)
		{
			return Create(book, chapterNumber1Based, null);
		}

		/// <summary>
		/// This version is useful mainly in tests. It allows creating a ChapterInfo from simulated file contents (when source is non-null)
		/// </summary>
		/// <param name="book">Info about the book containing this chapter</param>
		/// <param name="chapterNumber1Based">[0] == intro, [1] == chapter 1, etc.</param>
		/// <param name="source">If non-null, this will be used rather than the standard file as the source of chapter information.</param>
		public static ChapterInfo Create(BookInfo book, int chapterNumber1Based, string source)
		{
			ChapterInfo chapterInfo = null;
			string filePath = Path.Combine(book.GetChapterFolder(chapterNumber1Based), kChapterInfoFilename);
			if (File.Exists(filePath) || !string.IsNullOrEmpty(source))
			{
				try
				{
					if (string.IsNullOrEmpty(source))
						chapterInfo = XmlSerializationHelper.DeserializeFromFile<ChapterInfo>(filePath); // normal
					else
						chapterInfo = XmlSerializationHelper.DeserializeFromString<ChapterInfo>(source); // tests
					int prevLineNumber = 0;
					int countOfRecordings = chapterInfo.Recordings.Count;
					for (int i = 0; i < countOfRecordings; i++)
					{
						ScriptLine block = chapterInfo.Recordings[i];
						if (block.Number <= prevLineNumber)
						{
							var backup = Path.ChangeExtension(filePath, "corrupt");
							File.Delete(backup);
							File.Move(filePath, Path.ChangeExtension(filePath, "corrupt"));
							chapterInfo.Recordings.RemoveRange(i, countOfRecordings - i);
							chapterInfo.Save(filePath);
							break;
						}
						prevLineNumber = block.Number;
					}
				}
				catch (Exception e)
				{
					Analytics.ReportException(e);
					Debug.Fail(e.Message);
				}
			}
			if (chapterInfo == null)
			{
				chapterInfo = new ChapterInfo();
				chapterInfo.ChapterNumber1Based = chapterNumber1Based;
				chapterInfo.Recordings = new List<ScriptLine>();
			}

			chapterInfo._projectName = book.ProjectName;
			chapterInfo._bookName = book.Name;
			chapterInfo._bookNumber = book.BookNumber;
			chapterInfo._scriptProvider = book.ScriptProvider;
			chapterInfo._filePath = filePath;

			return chapterInfo;
		}

		internal void UpdateSource()
		{
			var scriptBlockCount = _scriptProvider.GetScriptBlockCount(_bookNumber, ChapterNumber1Based);
			Source = new List<ScriptLine>(scriptBlockCount);
			for (int i = 0; i < scriptBlockCount; i++)
			{
				Source.Add(_scriptProvider.GetBlock(_bookNumber, ChapterNumber1Based, i));
			}
		}

		public bool IsEmpty
		{
			get { return GetScriptBlockCount() == 0; }
		}

		public int GetScriptBlockCount()
		{
			return _scriptProvider.GetScriptBlockCount(_bookNumber, ChapterNumber1Based);
		}

		/// <summary>
		/// "Recorded" actually means either recorded or skipped.
		/// </summary>
		/// <returns></returns>
		public int CalculatePercentageRecorded()
		{
			int skippedScriptLines = 0;
			int scriptLineCount = GetScriptBlockCount();
			if (scriptLineCount == 0)
				return 0;

			for (int i = 0; i < scriptLineCount; i++)
			{
				if (_scriptProvider.GetBlock(_bookNumber, ChapterNumber1Based, i).Skipped)
					skippedScriptLines++;
			}
			// ENHANCE: This was causing too many problems because the Recordings list isn't being maintained the case where
			// a settings change causes the block breaks to change. This can result in a recording whose block number is now
			// the same as that of a previously skipped block. This block will then be double-counted (once as a skip and once
			// as a recording). This isn't a very common scenario and it might be okay, but the performance gain here is modest,
			// and it's not clear whether it's worth this potential confusion. I think the ideal solution is to somehow fix up
			// the Recordings list when a settings change causes shifting of blocks, but that's more than I'm prepared to do at
			// this stage. In any case, until we have the pieces in place for the user to do the necessary clean-up (moving
			// recordings to align properly with new block divisions), it's not going to be possible to get things right.
			//// First check Recordings collection in memory - it's faster (though not guaranteed reliable since someone could delete a file).
			//if (Recordings.Count + skippedScriptLines == scriptLineCount)
			//    return 100;

			return (int)(100 * (ClipRepository.GetCountOfRecordingsInFolder(Path.GetDirectoryName(_filePath)) + skippedScriptLines)/
				(float)(scriptLineCount));
		}

		public bool RecordingsFinished
		{
			get
			{
				int scriptLineCount = GetScriptBlockCount();
				// First check Recordings collection in memory - it's faster (though not guaranteed reliable since someone could delete a file).
				if (Recordings.Count == scriptLineCount)
					return true;
				// Older versions of HT didn't maintain in-memory recording info, so see if we have the right number of recordings.
				// ENHANCE: for maximum reliability, we should check for the existence of the exact filenames we expect.
				return CalculatePercentageRecorded() >= 100;
			}
		}

		public int CalculatePercentageTranslated()
		{
			 return (_scriptProvider.GetTranslatedVerseCount(_bookNumber, ChapterNumber1Based));
		}

		public void MakeDummyRecordings()
		{
			using (TempFile sound = new TempFile())
			{
				byte[] buffer = new byte[Resources.think.Length];
				Resources.think.Read(buffer, 0, buffer.Length);
				File.WriteAllBytes(sound.Path, buffer);
				for (int line = 0; line < GetScriptBlockCount(); line++)
				{
					var path = ClipRepository.GetPathToLineRecording(_projectName, _bookName, ChapterNumber1Based, line);

					if (!File.Exists(path))
					{
						File.Copy(sound.Path, path, false);
						OnScriptBlockRecorded(_scriptProvider.GetBlock(_bookNumber, ChapterNumber1Based, line));
					}
				}
			}
		}

		private String Folder
		{
			get { return ClipRepository.GetChapterFolder(_projectName, _bookName, ChapterNumber1Based); }
		}

		private String FilePath
		{
			get { return _filePath; }
		}

		public void RemoveRecordings()
		{
			Directory.Delete(Folder, true);
			Recordings = Recordings.Where(r => r.Skipped).ToList();
			Save();
		}

		private void Save()
		{
			Save(FilePath);
		}

		private void Save(string filePath)
		{
			XmlSerializationHelper.SerializeToFile(filePath, this);
		}

		public string ToXmlString()
		{
			return XmlSerializationHelper.SerializeToString(this);
		}

		public void OnScriptBlockRecorded(ScriptLine selectedScriptBlock)
		{
			selectedScriptBlock.Skipped = false;
			Debug.Assert(selectedScriptBlock.Number > 0);
			int iInsert = 0;
			for (int i = 0; i < Recordings.Count; i++)
			{
				var recording = Recordings[i];
				if (recording.Number == selectedScriptBlock.Number)
				{
					Recordings[i] = selectedScriptBlock;
					iInsert = -1;
					break;
				}
				if (recording.Number > selectedScriptBlock.Number)
				{
					break;
				}
				iInsert++;
			}
			if (iInsert >= 0)
				Recordings.Insert(iInsert, selectedScriptBlock);
			Save();
		}

		public void OnClipDeleted(ScriptLine selectedScriptBlock)
		{
			var recording = Recordings.FirstOrDefault(r => r.Number == selectedScriptBlock.Number);
			if (recording != null)
				Recordings.Remove(recording);
		}
	}
}