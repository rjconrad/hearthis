using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Paratext;

namespace HearThis.Script
{
	/// <summary>
	/// This class is used in ParatextScriptProvider.LoadBook. It manages the accumulation of text from multiple
	/// input tokens (markers or the following text tokens) and splitting the accumulated text of a paragraph
	/// into ScriptLines (that is, segments of an appropriate length and content to attempt to record as a unit).
	/// </summary>
	public class ParatextParagraph
	{
		//this was unreliable as the inner format stuff is apparently a reference, so it would change unintentionally
		//public ScrParserState State { get; private set; }
		public ScrTag State { get; private set; }
		public string text { get; private set; }
		private int _initialLineNumber0Based;
		private int _finalLineNumber0Based;
		private readonly HashSet<string> _introHeadingStyles = new HashSet<string> { "is", "imt", "imt1", "imt2", "imt3", "imt4", "imte", "imte1", "imte2", "is1", "is2", "iot" };

		private string _verse = "0";

		// Used to keep track of where new verses start
		class VerseStart
		{
			public string Verse;
			public int Offset;
		}

		List<VerseStart> _starts = new List<VerseStart>();

		public string Verse
		{
			get { return _verse; }
			set
			{
				_verse = value;
				NoteVerseStart();
			}
		}

		private void NoteVerseStart()
		{
			_starts.Add(new VerseStart() {Verse = _verse, Offset = (text ?? "").Length});
		}

		public bool HasData
		{
			get { return !string.IsNullOrEmpty(text); }
		}

		public void AddHardLineBreak()
		{
			Add(ScriptLine.kLineBreak.ToString(CultureInfo.InvariantCulture));
			ContainsHardLineBreaks = true;
		}

		public void Add(string s)
		{
			if (State == null || _finalLineNumber0Based > _initialLineNumber0Based)
				throw new InvalidOperationException("Must call StartNewParagraph before adding Text to ParatextParagraph.");
			text += s;
			//Debug.WriteLine("Add " + s + " : " + State.Marker + " bold=" + State.Bold + " center=" + State.JustificationType);
		}

		public void StartNewParagraph(IScrParserState scrParserState, bool resetLineNumber)
		{
			if (HasData && _finalLineNumber0Based == _initialLineNumber0Based)
			{
				var bldr = new StringBuilder();
				bool fFirstTime = true;
				foreach (ScriptLine item in BreakIntoBlocks())
				{
					if (item != null)
					{
						string sItem = item.Text;
						if (!fFirstTime && (!string.IsNullOrEmpty(sItem)))
							bldr.Append(" ");
						bldr.Append(sItem);
					}
					fFirstTime = bldr.Length == 0;
				}
				Debug.Fail("Looks like BreakIntoLines never got called for paragraph: " + bldr);
			}
			text = string.Empty;
			ContainsHardLineBreaks = false;
			_starts.Clear();
			NoteVerseStart();
			State = scrParserState.ParaTag;
			_initialLineNumber0Based = resetLineNumber ? 0 : _finalLineNumber0Based;
			_finalLineNumber0Based = _initialLineNumber0Based;

			//              Debug.WriteLine("Start " + State.Marker + " bold=" + State.Bold + " center=" + State.JustificationType);
		}

		/// <summary>
		/// Break the input into (trimmed) blocks at sentence-final punctuation.
		/// Sentence-final punctuation which occurs before any non-white text is attached to the following sentence.
		/// Also responsible to convert double angle brackets to proper double-quote characters,
		/// and keep them attached to the right sentences.
		/// Possible enhancements:
		///		- Handle converting single angle brackets to appropriate single quotes
		///			- Handle special case of >>> (single followed by double)
		///		- Handle non-Roman sentence-final punctuation
		///		- Keep other post-end-of-sentence characters attached (closing parenthesis? >>>?
		/// </summary>
		/// <returns></returns>
		public IEnumerable<ScriptLine> BreakIntoBlocks()
		{
			// Note... while one might think that char.GetUnicodeCategory could tell you if a character was a sentence separator, this is not the case.
			// This is because, for example, '.' can be used for various things (abbreviation, decimal point, as well as sentence terminator).
			var separators = new [] { '.', '?', '!',
				'।', '॥' //devenagri
			};
			// REVIEW: This will probably not be needed if we get Paratext data via plug-in interface.
			// Common way of representing quotes in Paratext. The >>> combination is special to avoid getting the double first;
			// <<< is not special as the first two are correctly changed to double quote, then the third to single.
			// It is, of course, important to do all the double replacements before the single, otherwise, the single will just match doubles twice.
			var input = text.Replace(">>>","’”").Replace("<<", "“").Replace(">>", "”").Replace("<","‘").Replace(">","’").Trim();
			_finalLineNumber0Based = _initialLineNumber0Based;
			foreach (var chunk in SentenceClauseSplitter.BreakIntoChunks(input, separators))
			{
				var x = GetScriptLine(chunk.Text, _finalLineNumber0Based++);
				SetScriptVerse(x, chunk.Start);
				yield return x;
			}
		}

		private void SetScriptVerse(ScriptLine block, int start)
		{
			if (_starts.Count == 0)
			{
				block.Verse = Verse;
				return; // not sure this can happen, playing safe.
			}
			var verse = _starts[0].Verse;
			for (int i = 0; i < _starts.Count; i++)
			{
				if (_starts[i].Offset > start)
					break;
				verse = _starts[i].Verse;
			}
			block.Verse = verse;
		}

		private ScriptLine GetScriptLine(string s, int lineNumber0Based)
		{
			//Debug.WriteLine("Emitting "+s+" bold="+State.Bold+" center="+State.JustificationType);
			var fontName = (string.IsNullOrWhiteSpace(State.Fontname)) ? DefaultFont : State.Fontname;
			return new ScriptLine()
			{
				Number = lineNumber0Based + 1,
				Text = s,
				Bold = State.Bold,
				// For now we want everything aligned left. Otherwise it gets separated from the hints that show which bit to read.
				Centered = false, //State.JustificationType == ScrJustificationType.scCenter,
				FontSize = State.FontSize,
				FontName = fontName,
				Heading = IsHeading,
				ForceHardLineBreakSplitting = ContainsHardLineBreaks
			};
		}

		public bool ContainsHardLineBreaks { get; private set; }

		private bool IsHeading
		{
			get
			{
				if (State.TextType == ScrTextType.scTitle || State.TextType == ScrTextType.scSection || State.HasTextProperty(TextProperties.scChapter))
					return true;
				return (_introHeadingStyles.Contains(State.Marker));
			}
		}

		private string _defaultFont;
		public string DefaultFont
		{
			get { return _defaultFont ?? ""; }
			set { _defaultFont = value; }
		}

		internal void ImproveState(IScrParserState state)
		{
			if (State.TextType == ScrTextType.scTitle && state.ParaTag != null && state.ParaTag.TextType == ScrTextType.scTitle)
			{
				State = state.ParaTag;
			}
			else
			{
				throw new InvalidOperationException(string.Format("Invalid state change attempted from {0} to {1}",
					State.Marker, state.ParaTag != null ? state.ParaTag.Marker : "null"));
			}
		}
	}
}
