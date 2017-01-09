﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Data;
using System.IO;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Diagnostics;

namespace WordSend
{
    public class Usx2Usfx
    {

        XmlTextReader usx;
        Scriptures scrp;

        /// <summary>
        /// Get an attribute with the given name from the current usx element, or an empty string if it is not present.
        /// </summary>
        /// <param name="attributeName">attribute name</param>
        /// <returns>attribute value or an empty string if not found</returns>
        protected string GetAnAttribute(string attributeName)
        {
            string result = usx.GetAttribute(attributeName);
            if (result == null)
                result = String.Empty;
            return result;
        }

        void CloseEmptyElement()
        {
            if (usx.IsEmptyElement)
                scrp.xw.WriteEndElement();
        }

        /// <summary>
        /// Takes a string like "PSA 2:7" from the loc attribute of a USX ref tag and 
        /// converts it to a string like "PSA.2.7" for the tgt attribute of a USFX ref tag.
        /// </summary>
        /// <param name="loc">USX ref loc reference target</param>
        /// <returns>USFX ref tgt reference target</returns>
        string usxLoc2usfxTgt(string loc)
        {
            string result = loc.Replace(' ', '.').Replace(':', '.').Replace("a", "").Replace("b", "");
            if (result.Contains(".-"))
                return string.Empty;
            return result;
        }

        string reftgt;

        /// <summary>
        /// Read a USX file, convert to USFX, and append to a USFX file.
        /// </summary>
        /// <param name="UsxFileName">Name of one USX file to read</param>
        /// <returns>true iff the conversion worked</returns>
        protected bool ReadUsx(string UsxFileName)
        {
            int charNesting = 0;
            int noteCharNesting = 0;
            string style;
            string number;
            string code;
            string sfm;
            string level;
            string caller;
            string loc;
            string closed;
            string thisBook = String.Empty;
            string thisChapter = String.Empty;
            string thisVerse = String.Empty;
            bool badNoteCharSyntaxUsed = false;
            bool inNote = false;
            try
            {
                usx = new XmlTextReader(UsxFileName);
                usx.WhitespaceHandling = WhitespaceHandling.Significant;
                while (usx.Read())
                {
                    if (usx.NodeType == XmlNodeType.Element)
                    {
                        style = GetAnAttribute("style");
                        number = GetAnAttribute("number");
                        code = GetAnAttribute("code");
                        caller = GetAnAttribute("caller");
                        closed = GetAnAttribute("closed");
                        loc = GetAnAttribute("loc");
                        switch (usx.Name)
                        {
                                // TODO: Handle: rem, cl, cp, ca, va, vp
                            case "usx": // Ignore this one and use </usx> to close the <book> tag.
                                break;
                            case "book":    // In usfx, <book> is a container around a book.
                                            // In usx, <book> is encompasses only the \id line
                                if (processedUsxBooks.Contains(code))
                                {
                                    usx.Close();
                                    return false;    // Skipping book because we read it already in another canon set
                                }
                                processedUsxBooks += code + " ";    // Keep track of books already processed.

                                scrp.xw.WriteStartElement("book");
                                scrp.xw.WriteAttributeString("id", code);
                                scrp.xw.WriteStartElement("id");
                                scrp.xw.WriteAttributeString("id", code);
                                thisBook = code;
                                thisChapter = thisVerse = "0";
                                CloseEmptyElement();
                                break;
                            case "chapter":
                                scrp.xw.WriteStartElement(style);
                                scrp.xw.WriteAttributeString("id", number);
                                thisChapter = number;
                                thisVerse = "0";
                                CloseEmptyElement();
                                break;
                            case "verse":
                                number = number.Replace(',', '-');  // Paratext allows comma or dash as a separator in verse ranges.
                                scrp.xw.WriteStartElement(style);
                                scrp.xw.WriteAttributeString("id", number);
                                thisVerse = number;
                                CloseEmptyElement();
                                /*
                                if ((thisBook == "ACT") && (thisChapter == "11") && (thisVerse == "11"))
                                    Logit.WriteLine("Acts 11:11");
                                 */
                                break;
                            case "note":
                                scrp.xw.WriteStartElement(style);
                                scrp.xw.WriteAttributeString("caller", caller);
                                scrp.xw.WriteAttributeString("sfm", style);
                                badNoteCharSyntaxUsed = false;
                                inNote = true;
                                CloseEmptyElement();
                                break;
                            case "char":
                                scrp.xw.WriteStartElement(style);
                                if (!usx.IsEmptyElement)
                                {
                                    if (inNote)
                                        noteCharNesting++;
                                    else
                                        charNesting++;
                                }
                                if ((closed == "false") && (usx.IsEmptyElement))
                                {
                                    badNoteCharSyntaxUsed = true;
                                    Logit.WriteError("Empty unclosed char element at " + thisBook + " " + thisChapter + ":" + thisVerse);
                                }
                                else
                                {
                                    CloseEmptyElement();
                                }
                                break;
                            case "table":
                                scrp.xw.WriteStartElement("table");
                                CloseEmptyElement();
                                break;
                            case "row":
                                scrp.xw.WriteStartElement(style);
                                CloseEmptyElement();
                                break;
                            case "cell":
                                scrp.xw.WriteStartElement(style);
                                CloseEmptyElement();
                                break;
                            case "para":
                                level = String.Empty;
                                sfm = style;
                                int lastDigitIndex = style.Length - 1;
                                if (char.IsDigit(style[lastDigitIndex]))
                                {
                                    level = style.Substring(lastDigitIndex);
                                    sfm = style.Substring(0,lastDigitIndex);
                                }
                                switch (sfm)
                                {
                                    case "h":
                                        scrp.xw.WriteStartElement("h");
                                        break;
                                    case "toc":
                                        if (level == String.Empty)
                                            level = "1";
                                        scrp.xw.WriteStartElement("toc");
                                        scrp.xw.WriteAttributeString("level", level);
                                        break;
                                    case "p":
                                    case "q":
                                    case "d":
                                    case "s":
                                    case "mt":
                                        scrp.xw.WriteStartElement(sfm);
                                        if (!String.IsNullOrEmpty(level))
                                            scrp.xw.WriteAttributeString("level", level);
                                        break;
                                    case "restore":
                                        // Discard this paragraph: it is a useless comment, not USFM, meaningless for publishing, and deprecated in current Paratext use
                                        if (!usx.IsEmptyElement)
                                        {
                                            bool stillMore = true;
                                            while (stillMore && !(usx.NodeType == XmlNodeType.EndElement))
                                                stillMore = usx.Read();
                                        }
                                        break;
                                    default:
                                        scrp.xw.WriteStartElement("p");
                                        scrp.xw.WriteAttributeString("sfm", sfm);
                                        if (!String.IsNullOrEmpty(level))
                                            scrp.xw.WriteAttributeString("level", level);
                                        break;
                                }
                                CloseEmptyElement();
                                break;
                            case "figure":
                                scrp.xw.WriteStartElement(style);
                                string s = GetAnAttribute("desc");
                                scrp.xw.WriteElementString("description", s);
                                s = GetAnAttribute("file");
                                scrp.xw.WriteElementString("catalog", s);
                                s = GetAnAttribute("size");
                                scrp.xw.WriteElementString("size", s);
                                s = GetAnAttribute("loc");
                                scrp.xw.WriteElementString("location", s);
                                s = GetAnAttribute("copy");
                                scrp.xw.WriteElementString("copyright", s);
                                s = GetAnAttribute("ref");
                                scrp.xw.WriteElementString("reference", s);
                                if (!usx.IsEmptyElement)
                                {
                                    usx.Read();
                                    if (usx.NodeType == XmlNodeType.Text)
                                    {
                                        scrp.xw.WriteElementString("caption", usx.Value);
                                    }
                                    else if (usx.NodeType == XmlNodeType.EndElement)
                                    {
                                        scrp.xw.WriteEndElement();
                                        if (usx.Name != "figure")
                                        {
                                            Logit.WriteError("Unexpected tag after figure: " + usx.Name);
                                        }
                                    }
                                    else
                                    {
                                        Logit.WriteError("Unexpected node type reading caption of figure!");
                                    }
                                }
                                CloseEmptyElement();
                                break;
                            case "optbreak":
                                scrp.xw.WriteStartElement("optionalLineBreak");
                                CloseEmptyElement();
                                break;
                            case "ref":
                                reftgt = usxLoc2usfxTgt(loc);
                                if (reftgt.Length > 6)
                                {
                                    scrp.xw.WriteStartElement("ref");
                                    scrp.xw.WriteAttributeString("tgt", reftgt);
                                }
                                break;
                            default:
                                Logit.WriteError("Unrecognized USX element name: " + usx.Name);
                                break;
                        }
                    }
                    else if (usx.NodeType == XmlNodeType.EndElement)
                    {
                        if (usx.Name == "ref")
                        {
                            if (reftgt.Length > 6)
                                scrp.xw.WriteEndElement();
                        }
                        else
                        {
                            if (usx.Name == "char")
                            {
                                if (inNote)
                                    noteCharNesting--;
                                else
                                    charNesting--;
                            }
                            if ((noteCharNesting < 0) || (charNesting < 0))
                                Logit.WriteError(String.Format("Unexpected char nesting value: {0} normal {1} in notes", charNesting, noteCharNesting));
                            if ((usx.Name == "note") && badNoteCharSyntaxUsed)
                            {
                                inNote = false;
                                if (badNoteCharSyntaxUsed)
                                {
                                    scrp.xw.WriteEndElement();  // Close the character style started with a milestone. Yukky syntax.
                                    badNoteCharSyntaxUsed = false;
                                }
                            }
                            scrp.xw.WriteEndElement();
                        }
                    }
                    else if ((usx.NodeType == XmlNodeType.SignificantWhitespace) ||
                        (usx.NodeType == XmlNodeType.Whitespace) ||
                        (usx.NodeType == XmlNodeType.Text))
                    {
                        scrp.xw.WriteString(usx.Value);
                    }
                }


                usx.Close();
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error reading " + UsxFileName);
                Logit.WriteError(ex.Message);
                return false;
            }


            return true;
        }

        string processedUsxBooks;

        /// <summary>
        /// Convert all USX files with .usx extensions in the given usxDir and up to one
        /// directory below it to USFX in usfxFile. (The one directory below it is to
        /// allow a pure unzip of an ETEN DBL bundle to be put into the USX directory.
        /// </summary>
        /// <param name="usxDir">Directory containing .usx files</param>
        /// <param name="usfxFile">path and file name of USFX file to write</param>
        /// <returns></returns>
        public bool Convert(string usxDir, string usfxFile)
        {
            try
            {
                processedUsxBooks = string.Empty;
                scrp = new Scriptures();
                scrp.OpenUsfx(usfxFile);
                DirectoryInfo dir = new DirectoryInfo(usxDir);
                foreach (FileInfo f in dir.GetFiles())
                {
                    if (f.Extension.ToLower().CompareTo(".usx") == 0)
                        ReadUsx(f.FullName);
                }
                foreach (DirectoryInfo di in dir.GetDirectories())
                {
                    string fullName = Path.Combine(usxDir, di.Name);
                    if (Directory.Exists(fullName))
                    {
                        DirectoryInfo d2 = new DirectoryInfo(fullName);
                        foreach (FileInfo f2 in d2.GetFiles())
                        {
                            if (f2.Extension.ToLower().CompareTo(".usx") == 0)
                                ReadUsx(f2.FullName);
                        }
                    }
                }



                scrp.CloseUsfx();
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error converting USX files in " + usxDir + " to " + usfxFile);
                Logit.WriteError(ex.Message);
                return false;
            }
            return true;
        }
    }
}