﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using WordSend;

namespace BibleFileLib
{
	/// <summary>
	/// This subclass overrides various methods to generate HTML in a frame system.
	/// At the root, index.htm is a frame containing a navigation pane, Navigation.htm, above another frame page, Root.htm.
	/// Root.htm contains a left pane ChapterIndex.htm, and a right pane introduction.htm.
	/// 
	/// ChapterIndex.htm (separately generated by UsfxToChapterIndex) contains links to a set of parallel frame files, one set for each chapter:
	/// main_IDNN.htm is the top-level frame for book ID chapter NN (there's also main_IDTOC.htm for a table of contents, and sometimes main_IDIntroduction.htm
	/// for an introduction to the book). They follow the pattern:
	/// main_IDNN.htm is a frame holding Navigation.htm above Root_IDNN.htm,
	/// Root_IDNN.htm is a frame holding ChapterIndex.htm beside IDNN.htm, the actual chapter content.
	/// 
	/// The actual content of IDNN.htm is also modified by overrides here, so that it does not contain a full set of navigation controls,
	/// but just Previous, Next, and Show Navigation Panes buttons. (JavaScript should change the Show Navigation Panes button to
	/// Hide Navigation Panes when the page is properly framed.)
	/// </summary>
	public class UsfxToFramedHtmlConverter : usfxToHtmlConverter
	{
		private const string MainFramePrefix = "frame_"; // Top level frame files, pair navigation with interior frame
		private const string InteriorFramePrefix = "root_"; // interior frame files, pair chapter index with main content file
		private const string NavigationFileName = "Navigation.htm";

		public string PreviousChapterLinkText { get; set; }
		public string NextChapterLinkText { get; set; }
		public string HideNavigationButtonText { get; set; }
		public string ShowNavigationButtonText { get; set; }

		public UsfxToFramedHtmlConverter()
		{
			PreviousChapterLinkText = "<<";
			NextChapterLinkText = ">>";
			HideNavigationButtonText = "Hide Navigation Panes";
			ShowNavigationButtonText = "Show Navigation Panes";
		}

		/// <summary>
		/// Return the frame file to use for the specified book and chapter (including chapter 0 for the table of contents).
		/// This needs to be consistent with MainFileLinkTarget, but unfortunately we can't call that, because this method
		/// needs to be static, while the other needs to be virtual.
		/// </summary>
		/// <param name="bookId"></param>
		/// <param name="chap"></param>
		/// <returns></returns>
		public static string TopFrameName(string bookId, int chap)
		{
			return TopFrameName(HtmName(bookId, chap));
		}

		/// <summary>
		/// Return the frame file to use for the specified main file.
		/// This needs to be consistent with MainFileLinkTarget, but unfortunately we can't call that, because this method
		/// needs to be static, while the other needs to be virtual.
		/// </summary>
		/// <param name="mainFileName"></param>
		/// <returns></returns>
		public static string TopFrameName(string mainFileName)
		{
			return MainFramePrefix + mainFileName;
		}

		/// <summary>
		/// Return the file name to use as a link target when linking to the specified main-pane file.
		/// Overridden here to link to the frame we generate around that file.
		/// This needs to be consistent with TopFrameName, but unfortunately we can't make that call this, because this method
		/// needs to be virtual, while the other needs to be static.
		/// </summary>
		/// <param name="mainFileName"></param>
		/// <returns></returns>
		protected override string MainFileLinkTarget(string mainFileName)
		{
			return MainFramePrefix + mainFileName;
		}

		protected override string OnLoadArgument()
		{
			return string.Format(" onload=\"onLoadBook('{0}')\"", currentBookHeader);
		}

        /// <summary>
        /// Override to make the hot link affect the whole page, so we don't just update one pane, but put the right URL for the target
        /// chapter into the main URL box.
        /// </summary>
        protected override string HotlinkLeadIn
        {
            get
            {
                return "<a target=\"_top\" href=\"";
            }
        }

		/// <summary>
		/// Overrride for now to generate a master frame file. It expects to find an Introduction.htm for the main pane. Later we may generate the introduction itself, or copy one if found...
		/// </summary>
		/// <param name="translationId"></param>
		/// <param name="indexHtml"></param>
		/// <param name="goText"></param>
		protected override void GenerateIndexFile(string translationId, string indexHtml, string goText)
		{
			string indexFilePath = Path.Combine(htmDir, IndexFileName);
			string rootFileName = "root.htm";
			string rootFilePath = Path.Combine(htmDir, rootFileName);
			WriteFrameFile(indexFilePath, "rows=\"0, *\"", true, "navigation", NavigationFileName, "body", rootFileName, null);
		    var firstFrameContents = "Introduction.htm";
            if (!File.Exists(Path.Combine(htmDir, firstFrameContents)))
		    {
		        // We want to point the first frame instead at the first thing we have.
		        var firstBook = (from br in bookInfo.publishArray where br != null && br.HasContent select br).FirstOrDefault();
                if (firstBook != null)
                {
                    var bookId = firstBook.tla;
                    var tocFile = bookId + "00"; // Enhance: refactor to encapsulate this knowledge somewhere.
                    if (bookId == "PSA")
                        tocFile += "0";
                    if (File.Exists(Path.Combine(htmDir, Path.ChangeExtension(tocFile, "htm"))))
                        firstFrameContents = tocFile;
                    else
                    {
                        firstFrameContents = tocFile.Substring(0, tocFile.Length - 1) + "1"; // Review: what if no chapter 1?? A: This happens!
                    }
                }
		    }
			WriteFrameFile(rootFilePath, "cols=\"20%,80%\"", false, "index", UsfxToChapterIndex.ChapIndexFileName, "main",
                           Path.ChangeExtension(firstFrameContents, "htm"), null);

			// Also generate navigation file.
			var navPath = Path.Combine(htmDir, NavigationFileName);
			var htmNav = new StreamWriter(navPath, false, Encoding.UTF8);

			htmNav.WriteLine(
				"<!DOCTYPE html>");
			htmNav.WriteLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" >");
			htmNav.WriteLine("<head>");
			htmNav.WriteLine("    <title>Navigation Bar</title>");
			htmNav.WriteLine("      <style type=\"text/css\">");
			htmNav.WriteLine("         body {margin-top:2pt; margin-left:2pt; background:gray}");
			htmNav.WriteLine("         input {font-weight:bold}");
			htmNav.WriteLine("      </style>");
			htmNav.WriteLine("      <script src=\"Navigation.js\" type=\"text/javascript\"></script>");
			htmNav.WriteLine("</head>");
			htmNav.WriteLine("<body>");
            /*
			htmNav.WriteLine(
				"      <input type=\"button\" value=\"Go to start of Book\" title=\"Go to start of book\"");
			htmNav.WriteLine(
				"         onclick=\"gotoStartOfBook()\"/>");
			htmNav.WriteLine(
				"		<span id=\"book\" class=\"NavBookName\" style=\"background: lightgreen; padding-left:3pt; padding-right:3pt\" > </span>");
            */
			htmNav.WriteLine("</body>");
			htmNav.WriteLine("</html>");
			htmNav.Close();
		}

		public override void MakeFramesFor(string htmPath)
		{
			string htmName = Path.GetFileName(htmPath);
			string bookId = htmName.Substring(0, 3);
			string directory = Path.GetDirectoryName(htmPath);
			var topFrameName = MainFramePrefix + htmName;
			var topFramePath = Path.Combine(directory, topFrameName);
			var interiorFrameName = InteriorFramePrefix + htmName;
			var interiorFramePath = Path.Combine(directory, interiorFrameName);
			WriteFrameFile(topFramePath, "rows=\"0, *\" onload=\"onLoad()\"", true, "navigation", NavigationFileName, "body", interiorFrameName, "frameFuncs.js");
			// We put the bookId as the hash of the URL for the chapter index so that the current book is always visible.
			WriteFrameFile(interiorFramePath, "cols=\"20%,80%\"", false, "index", UsfxToChapterIndex.ChapIndexFileName + "#" + bookId, "main", htmName, null);
		}

        private string repeatedNavButtons;
		
		/// <summary>
		/// These files are displayed (typically) in a frame that supplies most navigation. We therefore generate a much simplified set.
		/// </summary>
		protected override void WriteNavButtons()
		{
            StringBuilder sb = new StringBuilder();
			int chapNumSize;
			var formatString = FormatString(out chapNumSize);
			string previousFileLink = null;
			string nextFileLink = null;
			string thisFile = null;
            string firstChapterFile = FirstChapterFile(formatString);

			for (int i = 0; i < chapterFileList.Count; i++)
			{
				string chFile = (string)chapterFileList[i];
				int cn;
				if (chFile.StartsWith(currentBookAbbrev))
				{
					if (int.TryParse(chFile.Substring(chFile.Length - chapNumSize), out cn))
                    {
                        if (cn == chapterNumber)
                        {
                            // This file is the one we are generating.
                            thisFile = chFile + ".htm";
                            if (hasContentsPage && (chapterNumber == 1))
                            {
                                int j = 0;
                                previousFileLink = currentBookAbbrev + j.ToString(formatString) + ".htm";
                            }
                            else if (i > 0)
                            {
                                previousFileLink = (string)chapterFileList[i - 1] + ".htm";
                            }
                            if (i < (chapterFileList.Count - 1))
                            {
                                nextFileLink = (string)chapterFileList[i + 1] + ".htm";
                                break;
                            }
                        }
                        else if (chapterNumber == 0)
                        {
                            thisFile = firstChapterFile;
                            nextFileLink = chFile + ".htm";
                            if (i > 0)
                            {
                                previousFileLink = (string)chapterFileList[i - 1] + ".htm";
                            }
                            break;
                        }
					}
				}
			}
            sb.Append("<div class=\"navButtons\">"+Environment.NewLine);
			if (!string.IsNullOrEmpty(previousFileLink))
			{
				sb.Append(
					"<input type=\"button\" value=\"" + PreviousChapterLinkText + "\" title=\"" + PreviousChapterLinkText +
					"\" onclick=\"top.location.href='" + TopFrameName(previousFileLink) + "'\"/>"+Environment.NewLine);
			}
            if (!string.IsNullOrEmpty(firstChapterFile))
            {
                sb.Append("<input type=\"button\" value=\"" + currentBookHeader + "\" title=\"" + currentBookHeader +
					"\" onclick=\"top.location.href='" + TopFrameName(firstChapterFile) + "'\"/>"+Environment.NewLine);
            }
			if (!string.IsNullOrEmpty(nextFileLink))
			{
				sb.Append(
					"<input type=\"button\" value=\"" + NextChapterLinkText + "\" title=\"" + NextChapterLinkText +
					"\" onclick=\"top.location.href='" + TopFrameName(nextFileLink) + "'\"/>");
			}
			htm.WriteLine(sb.ToString());
			htm.WriteLine("<input id='showNav' type=\"button\" value='" + ShowNavigationButtonText + "' title='" +
				            ShowNavigationButtonText + "' onclick=\"top.location.href='" + TopFrameName(thisFile) + "'\"/>");
			htm.WriteLine("<input id='hideNav' type=\"button\" value='" + HideNavigationButtonText + "' title='" +
							HideNavigationButtonText + "' onclick=\"top.location.href='" + thisFile + "'\"/>");		
            sb.Append("</div>");
            repeatedNavButtons = sb.ToString();
			htm.WriteLine("</div>");
		}

        /// <summary>
        /// Open an HTML file named with the given name if non-empty, or with the 3-letter book abbreviation followed by the chapter number then ".htm"
        /// and write the HTML header.
        /// </summary>
        /// <param name="fileName">Name of file to open if other than a Bible chapter.</param>
        /// <param name="mainScriptureFile">true iff TextFunc.js is to be included.</param>
        protected override void OpenHtmlFile(string fileName, bool mainScriptureFile, bool skipNav = false)
        {
            CloseHtmlFile();
            string chap = currentChapter;
            if ((fileName != null) && (fileName.Length > 0))
            {
                currentFileName = Path.Combine(htmDir, fileName);
            }
            else
            {
                if (currentBookAbbrev == "PSA")
                    chap = String.Format("{0}", chapterNumber.ToString("000"));
                else
                    chap = String.Format("{0}", chapterNumber.ToString("00"));
                currentFileName = Path.Combine(htmDir, currentBookAbbrev + chap + ".htm");
            }
            MakeFramesFor(currentFileName);
            htm = new StreamWriter(currentFileName, false, Encoding.UTF8);
            // It is important that the DOCTYPE declaration should be a single line, and that the <html> element starts the second line.
            // This is because the concordance parser uses a ReadLine to skip the DOCTYPE declaration in order to read the rest of the file as XML.
            // Note: we used XHTML in the framed version, instead of HTML 5, because frames are mostly deprecated in HTML 5.
            htm.WriteLine("<!DOCTYPE html>");
            htm.WriteLine("<html lang=\"{0}\" dir=\"{1}\">", shortLangId, textDirection);
            htm.WriteLine("<head>");
            htm.WriteLine("<meta charset=\"UTF-8\" />");
            //            htm.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            htm.WriteLine("<link rel=\"stylesheet\" href=\"{0}\" type=\"text/css\" />", customCssName);
            htm.WriteLine("<meta name=\"viewport\" content=\"user-scalable=yes, initial-scale=1, minimum-scale=1, width=device-width\"/>");
            //            htm.WriteLine("<meta name=\"viewport\" content=\"user-scalable=yes, initial-scale=1, minimum-scale=1, width=device-width, height=device-height\"/>");
            if (mainScriptureFile)
            {
                htm.WriteLine("<script src=\"TextFuncs.js\" type=\"text/javascript\"></script>");
            }
            htm.WriteLine("<title>{0} {1} {2}</title>",
                translationName, currentBookHeader, currentChapterPublished);
            htm.WriteLine(string.Format("<meta name=\"keywords\" content=\"{0}, {1}, Holy Bible, Scripture, Bible, Scriptures, New Testament, Old Testament, Gospel\" />",
                translationName, langId));
            htm.WriteLine("</head>");
            if (skipNav)
            {
                navButtonCode = String.Empty;
            }
            else
            {
                htm.WriteLine("<body class=\"mainDoc\"{0}>", OnLoadArgument());
                WriteNavButtons();
            }
        }


		protected override void RepeatNavButtons()
		{   // We use the stored string instead of computing the chapter links all over again, because the chapter number at this point is one higher.
            htm.WriteLine(repeatedNavButtons);
		}

		/// <summary>
		/// Creaete a frame file according to the parameters given
		/// </summary>
		/// <param name="framePath">Where to put the resulting file</param>
		/// <param name="frameSetParams">params for the frameset element, typically specifies layout of rows or columns</param>
		/// <param name="suppressFirstFrameScroll">true to put scrolling="no" on the first frame</param>
		/// <param name="firstFrameName"></param>
		/// <param name="firstFile">to put in the first frame</param>
		/// <param name="secondFrameName"></param>
		/// <param name="secondFile">to put in the second one.</param>
		private void WriteFrameFile(string framePath, string frameSetParams, bool suppressFirstFrameScroll, string firstFrameName, string firstFile, string secondFrameName, string secondFile, string javaScriptFile)
		{
			var htmFrame = new StreamWriter(framePath, false, Encoding.UTF8);
			htmFrame.WriteLine(
				"<!DOCTYPE html>");
			htmFrame.WriteLine("<html xmlns:msxsl=\"urn:schemas-microsoft-com:xslt\" xmlns:user=\"urn:nowhere\">");
			htmFrame.WriteLine("<head>");
			htmFrame.WriteLine("<META http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">");
			htmFrame.WriteLine("<meta name=\"viewport\" content=\"width=device-width\" />");
			if (!string.IsNullOrEmpty(javaScriptFile))
				htmFrame.WriteLine("<script src=\"" + javaScriptFile + "\" type=\"text/javascript\"></script>");
			htmFrame.WriteLine("<frameset " + frameSetParams + ">");
			string suppressScroll = suppressFirstFrameScroll ? " scrolling=\"no\"" : "";
			htmFrame.WriteLine(" <frame name=\"" + firstFrameName + "\" src=\"" + firstFile + "\"" + suppressScroll + ">");
			htmFrame.WriteLine(" <frame name=\"" + secondFrameName + "\" src=\"" + secondFile + "\"/>");
			htmFrame.WriteLine(" <noframes>");
			htmFrame.WriteLine(" <body>");
			// Could make this localizable, but we want it short...making lots of these files, and unlikely to be seen
			htmFrame.WriteLine(" <p>This needs a frame-capable browser.</p>");
			htmFrame.WriteLine(" </body>");
			htmFrame.WriteLine(" </noframes>");
			htmFrame.WriteLine("</frameset>");
			htmFrame.WriteLine("</html>");
			htmFrame.Close();
		}
	}
}
