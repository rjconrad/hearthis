using System;
using HearThis.Publishing;

namespace HearThis.Script
{
	public class BookInfo
	{
		private readonly string _projectName;
		private readonly string _name;
		public readonly int ChapterCount;

		/// <summary>
		/// [0] == intro, [1] == chapter 1, etc.
		/// </summary>
		private readonly int[] _versesPerChapter;
		private readonly IScriptProvider _scriptProvider;

		public BookInfo(string projectName, int number, string name, int chapterCount, int[] versesPerChapter, IScriptProvider scriptProvider)
		{
			BookNumber = number;
			_projectName = projectName;
			_name = name;
			ChapterCount = chapterCount;
			_versesPerChapter = versesPerChapter;
			_scriptProvider = scriptProvider;
		}

		public string LocalizedName
		{
			//TODO
			get { return _name; }
		}

		public int BookNumber { get; private set; }

		public bool HasVerses
		{
			get
			{
				//at the moment, we just look for verses in the first chapter

				for (int i = 0; i < new BibleStats().GetChaptersInBook(BookNumber + 1); i++)
				{
					if (_scriptProvider.GetTranslatedVerseCount(BookNumber, i + 1) >0)
					{
						return true;
					}
				}
				return false;
			}
		}



		public bool HasSomeRecordings
		{
			get
			{
				if (GetLineMethod == null)
				{
					var r = new Random();
					return r.Next(8) == 1;
				}
				return false;
			}
		}


		public bool HasAllRecordings
		{
			get
			{
				if (GetLineMethod == null)
				{
					var r = new Random();
					return r.Next(10) == 1;
				}
				return false;
			}
		}


		public Func<int, int, ScriptLine> GetLineMethod { get; set; }

//        /// <summary>
//        /// bool HasVersesMethod(chapter)
//        /// </summary>
//        public Func<int, int> VerseCountMethod { get; set; }

		public string Name
		{
			get { return _name; }

		}

		public bool HasIntroduction
		{
			get { return _scriptProvider.GetScriptLineCount(BookNumber, 0) >0; }
		}

		/// <summary>
		/// Gets intro or chapter 1, whichever comes first
		/// </summary>
		/// <returns></returns>
		public ChapterInfo GetFirstChapter()
		{
			if(HasIntroduction)
				return GetChapter(0);
			return GetChapter(1);
		}

		public virtual ChapterInfo GetChapter(int chapterOneBased)
		{
			int versesPossible = 0;
			if(chapterOneBased >0)//if not the intro material
				versesPossible = _versesPerChapter[chapterOneBased - 1];

			//todo: creating it new each time could lead to failed attempts to cache chapter information

			return new ChapterInfo(_projectName, Name, BookNumber, chapterOneBased,
				versesPossible, //note, this is still the possible verses, not the actual
				_scriptProvider);
		}

		public int CalculatePercentageRecorded()
		{
			var repo = new LineRecordingRepository();
			int scriptLineCount = _scriptProvider.GetScriptLineCount(BookNumber);
			if (scriptLineCount == 0)
				return 0;//should it be 0 or 100 or -1 or what?
			int countOfRecordingsForBook = repo.GetCountOfRecordingsForBook(_projectName, Name);
			if (countOfRecordingsForBook == 0)
				return 0;
			return Math.Max(1, (int)(100.0 * (float)countOfRecordingsForBook / scriptLineCount));
		}

		public int CalculatePercentageTranslated()
		{
			//todo

			if (_scriptProvider.GetTranslatedVerseCount(BookNumber, 1) > 0)
				return 100;
			return 0;
		}
	}
}