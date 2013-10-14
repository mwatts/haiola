﻿// --------------------------------------------------------------------------------------------
#region // Copyright (c) 2003-2013, SIL International, EBT, and Youth With A Mission
// <copyright from='2003' to='2013' company='SIL International, EBT, and Youth With A Mission'>
//    
//		Distributable under the terms of either the Common Public License or the
//		GNU Lesser General Public License, as specified in the LICENSING.txt file.
// </copyright>
#endregion
// File: BibleBookInfo.cs
// Responsibility: (Kahunapule) Michael P. Johnson
// Last reviewed: 
// 
// <remarks>
// Bible book metadata: names, versification, etc.
// </remarks>
// --------------------------------------------------------------------------------------------

using System;
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
    /// <summary>
    /// Structure holding information about one particular book of the Bible.
    /// Some of the information is more or less global, and some specific to
    /// one particular translation.
    /// </summary>
    public class BibleBookRecord
    {
        public int sortOrder;
        public int numChapters;
        public int[] verseCount;
        public string tla;  // Standard three letter abbreviation of book
        public string osisName;
        public string name; // Constant English long name
        public string shortName;    // Constant English short name
        public string shortCode;    // Constant English-like 2-char abbreviation
        private string swordName;   // Hard coded Sword Project English short name
        public int actualChapters;
        public string vernacularHeader; // From \h
        public string vernacularName;   // From \mt
        public string vernacularLongName; // from \toc1
        public string vernacularShortName;  // from \toc2
        public string vernacularAbbreviation;   // From \toc3
        public string vernacularAltName;    // From \ztoc4 or BookNames.xml alt attribute
        public string testament;
        public StringBuilder toc;
        public int publicationOrder;
        public ArrayList chapterFiles;  // Chapter file names only, i.e. PSA119.htm
        public ArrayList chaptersFound; // Contains ChapterInfo records of chapters in this book
        public bool isPresent;
        public bool includeThisBook;

        /// <summary>
        /// Constructor initalizes a (sort of) empty BibleBookRecord
        /// </summary>
        public BibleBookRecord()
        {
            sortOrder = publicationOrder = 0;
            numChapters = 151;
            actualChapters = 0;
            isPresent = false;
            tla = osisName = name = shortName = testament = vernacularAbbreviation = vernacularHeader = String.Empty;
            vernacularName = vernacularShortName = vernacularLongName = String.Empty;
            toc = new StringBuilder();
        }

        public string swordShortName
        {
            get {
                if (String.IsNullOrEmpty(swordName))
                {
                    if (shortName != null)
                        swordName = shortName.Replace("1", "I").Replace("2", "II").Replace("3", "III");
                }
                return (swordName);
            }
            set { swordName = value; }
        }

        /// <summary>
        /// Read only property returns true iff this book is present and nonempty
        /// </summary>
        public bool HasContent
        {
            get { return isPresent && ((chapterFiles != null && chapterFiles.Count > 0) || (testament == "x")); }
        }

        /// <summary>
        /// Returns true iff the listed chapter and verse are included in this translation.
        /// (This currently does not take into account missing verses within a chapter.)
        /// </summary>
        /// <param name="ch">Chapter number to check</param>
        /// <param name="vs">Verse number to check</param>
        /// <returns></returns>
        public bool isValidTarget(int ch, int vs)
        {
            bool result = false;
            int i;
            ChapterInfo ci;
            if (isPresent)
            {
                for (i = 0; (i < chaptersFound.Count) && (!result); i++)
                {
                    ci = (ChapterInfo)chaptersFound[i];
                    if (ci != null)
                    {
                        if (ci.chapterInteger == ch)
                        {
                            if (vs <= ci.maxVerse)
                                result = true;
                        }
                    }
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Information about books of the Bible in general and the current translation in particular, including
    /// names, versification, etc. It tracks the vernacular names and abbreviations of books, the versification
    /// and structure of the current project based on what is actually there for navigational purposes, and
    /// gathers a few statistics. Book order is assumed to be that which is given in BibleBookInfo.xml unless
    /// overridden by a bookorder.txt file in the project directory.
    /// </summary>
    public class BibleBookInfo
    {
        public const int MAXNUMBOOKS = 120;	// Includes Apocrypha + extrabiblical helps, front & back matter, etc.
        public Hashtable books;
        public BibleBookRecord[] bookArray = new BibleBookRecord[MAXNUMBOOKS];
        public BibleBookRecord[] publishArray = new BibleBookRecord[MAXNUMBOOKS];
        protected int publishArrayActualBookCount = -1;
        public Hashtable altNames;
        protected bool apocryphaFound;

        public int publishArrayCount
        {
            get
            {
                int i;
                if (publishArrayActualBookCount < 0)
                {
                    i = 0;
                    while ((publishArray[i] != null) && (i < publishArray.Length))
                    {
                        i++;
                    }
                    publishArrayActualBookCount = i;
                }
                return publishArrayActualBookCount;
            }
        }

        // protected HashSet<string> presentVerses;

        /// <summary>
        /// Normalize strings for comparison by removing spaces and periods and converting
        /// to all upper case.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        protected string PrepareToCompare(string s)
        {
            string result = s.Trim().ToUpperInvariant();
            result = s.Replace(" ", "");
            result = s.Replace(".", "");
            return result;
        }

        /// <summary>
        /// Find the standard three-letter abbreviation for a Bible book given any
        /// of its vernacular names (long, short, or abbreviation).
        /// </summary>
        /// <param name="book">vernacular name of Bible book</param>
        /// <returns>standard 3-letter abbreviation of Bible book</returns>
        public string getTla(string book)
        {
            string tla = (string)altNames[PrepareToCompare(book)];
            if (tla == null)
                tla = String.Empty;
            return tla;
        }

        /// <summary>
        /// Is this book an Apocrypha/Deuterocanon book?
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>true iff the book is part of the Apocrypha/Deuterocanon</returns>
        public bool isApocrypha(string abbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[abbrev];
            if (br == null)
                return false;
            return br.testament == "a";
        }

        /// <summary>
        /// Is this book a peripheral book (front matter, back matter, glossary, helps)?
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>true iff this is a peripheral book</returns>
        public bool isPeripheral(string abbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[abbrev];
            if (br == null)
                return false;
            return br.testament == "x";
        }

        /// <summary>
        /// What order should this book be sorted into for publication?
        /// The default order is like the NRSV with Apocrypha (not the RC edition),
        /// as presented in the BibleBookInfo.xml file in the program data,
        /// but can be overridden with the ReadPublicationOrder method.
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>integer indicating where this book belongs from front to back of the complete Bible + helps</returns>
        public int Order(string abbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[abbrev];
            if (br == null)
                return 0;
            return br.sortOrder;
        }

        /// <summary>
        /// Make up the first part of a file name that will sort in canonical order in a directory
        /// listing and which contains the standard 3-letter abbreviation of each book.
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>string like 01-GEN, 02-EXO, etc.</returns>
        public string FilePrefix(string abbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[abbrev];
            if (br == null)
                return "00-" + abbrev;
            int num = br.sortOrder;
            if (num < 40)
            {
                apocryphaFound = false;
                return num.ToString("d2") + "-" + abbrev;
            }
            if (num < 64)
            {
                apocryphaFound = true;
            }
            else
            {
                if (!apocryphaFound)
                    num -= 24;
            }
            return num.ToString("d2") + "-" + abbrev;
        }

        /// <summary>
        /// What is the variable-length OSIS ID for this book?
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>OSIS ID for this book or an empty string if not found</returns>
        public string OsisID(string abbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[abbrev];
            if (br == null)
                return "";
            return br.osisName;
        }

        /// <summary>
        /// Get the 2-character short code for a book given its 3-letter abbreviation
        /// </summary>
        /// <param name="bookAbbrev">standard UBS/SIL 3-character abbreviation</param>
        /// <returns>2-character short book code or "" if not found</returns>
        public string getShortCode(string bookAbbrev)
        {
            BibleBookRecord br = (BibleBookRecord)books[bookAbbrev];
            if (br == null)
                return "";
            return br.shortCode;
        }



        /// <summary>
        /// Returns true iff we are pretty sure this book, chapter, and verse are present.
        /// </summary>
        /// <param name="bk">Three-character book abbreviation</param>
        /// <param name="ch">chapter number</param>
        /// <param name="vs">verse number</param>
        /// <returns>true iff we think that verse is in this translation</returns>
        public bool isValidTarget(string bk, int ch, int vs)
        {
            BibleBookRecord br = (BibleBookRecord)books[bk];
            if (br == null)
                return false;
            if (!br.isPresent)
                return false;
            return br.isValidTarget(ch, vs);
        }

        /// <summary>
        /// Gets the full BibleBookRecord structure for the book with the given
        /// standard 3-letter abbreviation.
        /// </summary>
        /// <param name="abbrev">standard 3-letter book abbreviation</param>
        /// <returns>BibleBookRecord with information about this book</returns>
        public BibleBookRecord BkRec(string abbrev)
        {
            return (BibleBookRecord)books[abbrev];
        }

        /// <summary>
        /// Reads Bible book names from BookNames.xml, as exported by Paratext
        /// </summary>
        /// <param name="bookNamesFile">Full path and file name of BookNames.xml</param>
        /// <returns>true iff the file was found and read</returns>
        public bool ReadDefaultBookNames(string bookNamesFile)
        {
            XmlTextReader xr;
            bool inBookNames = false;
            string tla;
            BibleBookRecord br;

            if (!File.Exists(bookNamesFile))
                return false;
            try
            {
                xr = new XmlTextReader(bookNamesFile);
                xr.WhitespaceHandling = WhitespaceHandling.Significant;
                while (xr.Read())
                {
                    if (xr.Name == "BookNames")
                    {
                        inBookNames = xr.IsStartElement();
                    }
                    else if (inBookNames && xr.IsStartElement("book"))
                    {
                        tla = xr.GetAttribute("code");
                        if (!String.IsNullOrEmpty(tla))
                        {
                            br = BkRec(tla);
                            if (br != null)
                            {
                                br.vernacularAbbreviation = fileHelper.NoNull(xr.GetAttribute("abbr"));
                                br.vernacularShortName = fileHelper.NoNull(xr.GetAttribute("short"));
                                br.vernacularLongName = fileHelper.NoNull(xr.GetAttribute("long"));
                                br.vernacularAltName = fileHelper.NoNull(xr.GetAttribute("alt"));
                            }
                        }
                    }
                }
                xr.Close();
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error reading "+ bookNamesFile +"\r\n"+ ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Writes a BookNames.xml file in the same format as used by Paratext.
        /// </summary>
        /// <param name="bookNamesFile">Full path and name of XML file to write</param>
        public void WriteDefaultBookNames(string bookNamesFile)
        {
            XmlTextWriter xw;
            try
            {
                xw = new XmlTextWriter(bookNamesFile, Encoding.UTF8);
                xw.Formatting = Formatting.Indented;
                xw.WriteStartDocument();
                xw.WriteDocType("BookNames", null, null/* "ini.dtd" */, null);
                xw.WriteStartElement("BookNames");
                foreach (BibleBookRecord br in bookArray)
                {
                    if (br != null)
                    {
                        if ((br.testament != "x") && (!(String.IsNullOrEmpty(br.tla) || (String.IsNullOrEmpty(br.vernacularShortName)))))
                        {
                            xw.WriteStartElement("book");
                            xw.WriteAttributeString("code", br.tla);
                            xw.WriteAttributeString("abbr", fileHelper.NoNull(br.vernacularAbbreviation));
                            xw.WriteAttributeString("short", fileHelper.NoNull(br.vernacularShortName));
                            xw.WriteAttributeString("long", fileHelper.NoNull(br.vernacularLongName));
                            xw.WriteAttributeString("alt", fileHelper.NoNull(br.vernacularAltName));
                            xw.WriteEndElement();   // book
                        }
                    }
                }
                xw.WriteEndElement();   // BookNames
                xw.Close();
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error writing "+bookNamesFile+"\r\n"+ex.Message);
            }
        }



        /// <summary>
        /// Read Bible Book information from the indicated file assuming that it
        /// contains information about all supported books that might be bound in
        /// a Bible (both canonical books and certain extra stuff), assuming that
        /// it was written to the BibleBookInfo.xsd schema.
        /// </summary>
        /// <param name="fileName">Name of XML data file with Bible book information</param>
        public void ReadBookInfoFile(string fileName)
        {
            int i;
            books = new Hashtable(197);
            BibleBookRecord bkRecord = null;
            XmlTextReader xr = new XmlTextReader(fileName);
            xr.WhitespaceHandling = WhitespaceHandling.Significant;
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element)
                {
                    switch (xr.Name)
                    {
                        case "Book":
                            bkRecord = new BibleBookRecord();
                            for (i = 0; i < xr.AttributeCount; i++)
                            {
                                xr.MoveToAttribute(i);
                                if (xr.Name == "testament")
                                {
                                    bkRecord.testament = xr.Value;
                                }
                            }
                            xr.MoveToElement();
                            break;
                        case "sfmTla":
                            xr.Read();
                            bkRecord.tla = xr.Value;
                            break;
                        case "osisName":
                            xr.Read();
                            bkRecord.osisName = xr.Value;
                            break;
                        case "name":
                            xr.Read();
                            bkRecord.name = xr.Value;
                            break;
                        case "shortName":
                            xr.Read();
                            bkRecord.shortName = xr.Value;
                            break;
                        case "sortOrder":
                            xr.Read();
                            bkRecord.sortOrder = Convert.ToInt32(xr.Value);
                            break;
                        case "numChapters":
                            xr.Read();
                            bkRecord.numChapters = Convert.ToInt32(xr.Value);
                            bkRecord.verseCount = new int[155];
                            if ((bkRecord.sortOrder < 0) || (bkRecord.sortOrder >= BibleBookInfo.MAXNUMBOOKS))
                            {
                                Logit.WriteError("ERROR: bad sort order number:" + bkRecord.sortOrder.ToString());
                                bkRecord.sortOrder = 0;
                            }
                            break;
                        case "shortCode":
                            xr.Read();
                            bkRecord.shortCode = xr.Value;
                            break;
                    }
                }
                else if (xr.NodeType == XmlNodeType.EndElement)
                {
                    if (xr.Name == "Book")
                    {
                        books[bkRecord.tla] = bkRecord;
                        bookArray[bkRecord.sortOrder] = bkRecord;
                        publishArray[bkRecord.sortOrder] = bkRecord;    // Default book publication order
                    }
                }
            }
            xr.Close();
        }

        /// <summary>
        /// Instantiates a new instance of BibleBookInfo using data in the named file.
        /// </summary>
        /// <param name="fileName">BibleBookInfo.xml</param>
        public BibleBookInfo(string fileName)
        {
            ReadBookInfoFile(fileName);
        }

        /// <summary>
        /// Reads a file indicating the proper publication order for this translation instance.
        /// The file should be a plain text file, one line per book, with the standard 3-letter
        /// book abbreviation being the first 3 characters on the line, in the order that this
        /// Bible translation should be presented to the reader. All-blank lines or lines starting
        /// with anything other than a letter or digit are comments and are ignored. Anything
        /// after the first 3 nonblank characters of a line are ignored.
        /// </summary>
        /// <param name="fileName">text file to read</param>
        public void ReadPublicationOrder(string fileName)
        {
            int i = 0;
            BibleBookRecord br;

            string line;
            try
            {
                foreach (BibleBookRecord bkrec in bookArray)
                {
                    if (bkrec != null)
                        bkrec.includeThisBook = false;
                }
                StreamReader sr = new StreamReader(fileName);
                while (sr.Peek() >= 0)
                {
                    line = sr.ReadLine().Trim().ToUpperInvariant();
                    if (line.Length > 3)
                        line = line.Substring(0, 3);
                    if ((line.Length == 3) && Char.IsLetterOrDigit(line[0]) && (i < MAXNUMBOOKS))
                    {
                        br = (BibleBookRecord)books[line];
                        if (br == null)
                        {
                            Logit.WriteError("Bad abbreviation " + line + " in " + fileName);
                        }
                        else
                        {
                            br.publicationOrder = i;
                            br.isPresent = false;
                            br.includeThisBook = true;
                            publishArray[i] = br;
                            i++;
                        }
                    }
                }
                if (i < MAXNUMBOOKS)
                    publishArray[i] = null;
                publishArrayActualBookCount = i;
                sr.Close();
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error reading " + fileName + ex.Message);
            }
        }

        /// <summary>
        /// Instantiate a new BibleBookInfo object using data in BibleBookInfo.xml
        /// </summary>
        public BibleBookInfo()
        {
            ReadBookInfoFile(SFConverter.FindAuxFile("BibleBookInfo.xml"));
        }

        private string languageCode = string.Empty;

        /// <summary>
        /// Read-only property getting the current translation's 3-letter Ethnologue language code.
        /// </summary>
        public string ethnologueCode { get { return languageCode; } }

        public ArrayList allChapters;

        /// <summary>
        /// Reads vernacular book name and versification information from USFX file.
        /// Also tracks which chapters and verses are present.
        /// </summary>
        /// <param name="usfxName">Name of the USFX file to parse</param>
        public void ReadUsfxVernacularNames(string usfxName)
        {
            string level = String.Empty;
            string style = String.Empty;
            string sfm = String.Empty;
            string caller = String.Empty;
            string id = String.Empty;
            string currentBookAbbrev = String.Empty;
            string currentBookCode = String.Empty;  // Current book short code
            string bookOsisId = String.Empty;
            BibleBookRecord bookRecord = (BibleBookRecord)bookArray[0];
            string chapterString = String.Empty;
            int chapterNumber = 0;
            string verseString = String.Empty;
            string verseRangeEnd = String.Empty;
            string bookNamesFile = Path.Combine(Path.GetDirectoryName(usfxName), "BookNames.xml");
            int verseNumber = 0;
            int verseRangeEndNumber = 0;
            bool inParagraph = false;
            allChapters = new ArrayList(1195);  // Big enough for OT + NT + some peripherals. Reallocation will happen with Apocrypha/Deuterocanon.
            ChapterInfo ci = new ChapterInfo();

            // presentVerses = new HashSet<string>;

            try
            {

                foreach (BibleBookRecord br in bookArray)
                {
                    if (br != null)
                    {
                        br.vernacularAbbreviation = String.Empty;   // toc3
                        br.vernacularHeader = String.Empty; // h
                        br.vernacularLongName = String.Empty;   // toc1
                        br.vernacularName = String.Empty;   // mt
                        br.vernacularShortName = String.Empty;  // toc2
                        br.vernacularAltName = String.Empty;    // ztoc4
                    }
                }

                // Look for default names for books not in the USFX file.
                ReadDefaultBookNames(bookNamesFile);

                XmlTextReader usfx = new XmlTextReader(usfxName);
                usfx.WhitespaceHandling = WhitespaceHandling.Significant;
                altNames = new Hashtable(997);
                while (usfx.Read())
                {
                    if (usfx.NodeType == XmlNodeType.Element)
                    {
                        level = fileHelper.GetNamedAttribute(usfx, "level");
                        style = fileHelper.GetNamedAttribute(usfx, "style");
                        sfm = fileHelper.GetNamedAttribute(usfx, "sfm");
                        caller = fileHelper.GetNamedAttribute(usfx, "caller");
                        id = fileHelper.GetNamedAttribute(usfx, "id");

                        switch (usfx.Name)
                        {
                            case "languageCode":
                                usfx.Read();
                                if (usfx.NodeType == XmlNodeType.Text)
                                    languageCode = usfx.Value;
                                break;
                            case "book":
                                if (id.Length > 2)
                                {
                                    currentBookAbbrev = PrepareToCompare(id);
                                    bookRecord = (BibleBookRecord)books[currentBookAbbrev];
                                    bookRecord.chaptersFound = new ArrayList(151);
                                    bookOsisId = bookRecord.osisName;
                                    currentBookCode = bookRecord.shortCode;
                                    if (bookRecord == null)
                                    {
                                        Logit.WriteError("Cannot process unknown book \"" + currentBookAbbrev + "\" in " + usfxName);
                                        return;
                                    }
                                    altNames[currentBookAbbrev] = currentBookAbbrev;
                                }
                                chapterString = verseString = String.Empty;
                                chapterNumber = 0;
                                verseNumber = 0;
                                break;
                            case "id":
                                if (PrepareToCompare(id) != currentBookAbbrev)
                                {
                                    Logit.WriteError("ERROR: id element " + id + " <> " + " book id " + currentBookAbbrev + " in " + usfxName);
                                }
                                break;
                            case "p":
                                if (sfm.CompareTo("mt") == 0)
                                {
                                    usfx.Read();
                                    if (usfx.NodeType == XmlNodeType.Text)
                                    {
                                        if (bookRecord.vernacularName.Length > 0)
                                            bookRecord.vernacularName = bookRecord.vernacularName + " " + usfx.Value.Trim();
                                        else
                                            bookRecord.vernacularName = usfx.Value.Trim();
                                    }
                                    altNames[PrepareToCompare(bookRecord.vernacularName)] = currentBookAbbrev;
                                }
                                else
                                {
                                    inParagraph = true;
                                }
                                break;
                            case "h":
                                if (level != String.Empty)
                                    Logit.WriteLine("Warning: level not supported on h element.");
                                usfx.Read();
                                if (usfx.NodeType == XmlNodeType.Text)
                                {
                                    bookRecord.vernacularHeader = usfx.Value.Trim();
                                    altNames[PrepareToCompare(bookRecord.vernacularHeader)] = currentBookAbbrev;
                                }

                                break;
                            case "c":
                                chapterString = id.Trim();
                                verseString = verseRangeEnd = String.Empty;
                                verseNumber = verseRangeEndNumber = 0;
                                int chNum;
                                if (Int32.TryParse(chapterString, out chNum))
                                {
                                    chapterNumber = chNum;
                                }
                                else
                                {
                                    chapterNumber++;
                                    Logit.WriteError("Bad chapter number at " + currentBookAbbrev + " " + chapterString + " in " + usfxName);
                                }
                                bookRecord.actualChapters = Math.Max(bookRecord.numChapters, chapterNumber);
                                ci = new ChapterInfo();
                                ci.chapterInteger = chapterNumber;
                                ci.alternate = ci.actual = chapterString;
                                ci.published = chapterString;
                                ci.osisChapter = bookOsisId + "." + chapterNumber.ToString();
                                ci.chapterId = currentBookCode + chapterNumber.ToString();
                                ci.maxVerse = ci.verseCount = 0;
                                ci.bookRecord = bookRecord;
                                bookRecord.chaptersFound.Add(ci);
                                allChapters.Add(ci);
                                break;
                            case "cp":
                                if (!usfx.IsEmptyElement)
                                {
                                    usfx.Read();
                                    if (usfx.NodeType == XmlNodeType.Text)
                                    {
                                        ci.published = fileHelper.LocalizeDigits(usfx.Value.Trim());
                                    }
                                }
                                break;
                            case "ca":
                                if (!usfx.IsEmptyElement)
                                {
                                    usfx.Read();
                                    if (usfx.NodeType == XmlNodeType.Text)
                                    {
                                        ci.alternate = usfx.Value.Trim();
                                    }
                                }
                                break;
                            case "toc":
                                if (!usfx.IsEmptyElement)
                                {
                                    if (level == String.Empty)
                                        level = "1";
                                    usfx.Read();
                                    if (usfx.NodeType == XmlNodeType.Text)
                                    {
                                        switch (level)
                                        {
                                            case "1":
                                                bookRecord.vernacularLongName = usfx.Value.Trim();
                                                altNames[PrepareToCompare(bookRecord.vernacularLongName)] = currentBookAbbrev;
                                                break;
                                            case "2":
                                                bookRecord.vernacularShortName = usfx.Value.Trim();
                                                altNames[PrepareToCompare(bookRecord.vernacularShortName)] = currentBookAbbrev;
                                                break;
                                            case "3":
                                                bookRecord.vernacularAbbreviation = usfx.Value.Trim();
                                                altNames[PrepareToCompare(bookRecord.vernacularAbbreviation)] = currentBookAbbrev;
                                                break;
                                            case "4":
                                                bookRecord.vernacularAltName = usfx.Value.Trim();
                                                altNames[PrepareToCompare(bookRecord.vernacularAltName)] = currentBookAbbrev;
                                                break;
                                        }
                                    }
                                }
                                break;
                            case "v":
                                verseString = id.Trim();
                                int dashPlace = verseString.IndexOf('-');
                                if (dashPlace > 0)
                                {
                                    verseRangeEnd = verseString.Substring(dashPlace + 1);
                                    verseString = verseString.Substring(0, dashPlace);
                                }
                                else
                                {
                                    verseRangeEnd = verseString;
                                }
                                int vnum;
                                if (Int32.TryParse(verseString, out vnum))
                                {
                                    verseNumber = vnum;
                                }
                                else
                                {
                                    verseNumber++;
                                }
                                if (Int32.TryParse(verseRangeEnd, out vnum))
                                {
                                    verseRangeEndNumber = vnum;
                                }
                                else
                                {
                                    verseRangeEndNumber = verseNumber;
                                }
                                bookRecord.verseCount[chapterNumber] = verseRangeEndNumber;
                                ci.maxVerse = Math.Max(ci.maxVerse, verseRangeEndNumber);
                                ci.verseCount += verseRangeEndNumber - verseNumber + 1;

                                break;
                            case "x":

                                break;
                        }
                    }
                    else if (usfx.NodeType == XmlNodeType.Text)
                    {
                        if (inParagraph && (usfx.Value.Trim().Length > 0))
                        {
                            bookRecord.isPresent = true;
                        }
                    }
                    else if (usfx.NodeType == XmlNodeType.EndElement)
                    {
                        if (usfx.Name == "book")
                        {
                            if (bookRecord.vernacularName == String.Empty)
                            {
                                if ((bookRecord.testament == "o") || (bookRecord.testament == "n") || (bookRecord.testament == "a"))
                                {
                                    Logit.WriteError("Missing main title in " + currentBookAbbrev + " in " + usfxName);
                                }
                                bookRecord.vernacularName = bookRecord.vernacularLongName;
                                if (bookRecord.vernacularName == String.Empty)
                                {
                                    bookRecord.vernacularName = bookRecord.vernacularShortName;
                                }
                            }
                            if (bookRecord.vernacularLongName == String.Empty)
                            {
                                bookRecord.vernacularLongName = bookRecord.vernacularName;
                                // Logit.WriteLine("Missing toc1 (long title) element in " + currentBookAbbrev + " in " + usfxName);
                            }
                            if (bookRecord.vernacularShortName == String.Empty)
                            {
                                bookRecord.vernacularShortName = bookRecord.vernacularHeader;
                            }
                            if (bookRecord.vernacularShortName == String.Empty)
                            {
                                if (bookRecord.testament == "x")
                                {
                                    bookRecord.vernacularShortName = bookRecord.tla;
                                }
                                else
                                {
                                    bookRecord.vernacularShortName = bookRecord.vernacularLongName;
                                    Logit.WriteError("Missing vernacular short name toc2 or h in " + currentBookAbbrev + " in " + usfxName);
                                }
                            }
                            if (bookRecord.vernacularAbbreviation == String.Empty)
                            {
                                bookRecord.vernacularAbbreviation = bookRecord.vernacularShortName;
                            }
                            if ((bookRecord.isPresent) && (chapterNumber == 0))
                            {
                                ci = new ChapterInfo();
                                ci.chapterInteger = chapterNumber;
                                ci.alternate = ci.actual = chapterString;
                                ci.published = chapterString;
                                ci.osisChapter = bookOsisId + "." + chapterNumber.ToString();
                                ci.chapterId = currentBookCode + chapterNumber.ToString();
                                ci.maxVerse = ci.verseCount = 0;
                                ci.bookRecord = bookRecord;
                                bookRecord.chaptersFound.Add(ci);
                                allChapters.Add(ci);
                            }
                        }
                        else if (usfx.Name == "p")
                        {
                            inParagraph = false;
                        }
                    }
                    else if ((usfx.NodeType == XmlNodeType.Text) && (bookRecord != null) && (bookRecord.tla == currentBookAbbrev) && (usfx.Value.Trim().Length > 2))
                    {   // We don't count a book as present unless there is some text in it.
                        bookRecord.isPresent = true;
                    }
                    //conversionProgress = "navigation " + currentBookAbbrev + " " + currentChapter + ":" + currentVerse;
                    //System.Windows.Forms.Application.DoEvents();
                }
                usfx.Close();
                WriteDefaultBookNames(bookNamesFile);   // Override book names with toc tags
            }
            catch (Exception ex)
            {
                Logit.WriteError("Error reading vernacular file names from USFX\r\n"+ex.Message + "\r\n" + ex.StackTrace);
            }
        }

        public void RecordStats(Options m_options)
        {
            int i, j;
            int otBookCount = 0;
            int ntBookCount = 0;
            int adBookCount = 0;
            int pBookCount = 0;
            int otChapCount = 0;
            int ntChapCount = 0;
            int adChapCount = 0;
            int otVerseCount = 0;
            int ntVerseCount = 0;
            int adVerseCount = 0;
            int otVerseMax = 0;
            int ntVerseMax = 0;
            int adVerseMax = 0;
            BibleBookRecord br;
            ChapterInfo ci;
            for (i = 0; (i < publishArray.Length) && (publishArray[i] != null); i++)
            {
                br = (BibleBookRecord)publishArray[i];
                if (br.isPresent)
                {
                    switch (br.testament)
                    {
                        case "o":
                            otBookCount++;
                            otChapCount += br.chaptersFound.Count;
                            for (j = 0; j < br.chaptersFound.Count; j++)
                            {
                                ci = (ChapterInfo)br.chaptersFound[j];
                                if (ci != null)
                                {
                                    otVerseCount += ci.verseCount;
                                    otVerseMax += ci.maxVerse;
                                }
                            }
                            break;
                        case "n":
                            ntBookCount++;
                            ntChapCount += br.chaptersFound.Count;
                            for (j = 0; j < br.chaptersFound.Count; j++)
                            {
                                ci = (ChapterInfo)br.chaptersFound[j];
                                if (ci != null)
                                {
                                    ntVerseCount += ci.verseCount;
                                    ntVerseMax += ci.maxVerse;
                                }
                            }
                            break;
                        case "x":
                            pBookCount++;
                            break;
                        default:    // Testament = "a" or one of the other Apocrypha designations, like "Septuagint", "Vulgate", "Orthodox Canon"
                            adBookCount++;
                            adChapCount += br.chaptersFound.Count;
                            for (j = 0; j < br.chaptersFound.Count; j++)
                            {
                                ci = (ChapterInfo)br.chaptersFound[j];
                                if (ci != null)
                                {
                                    adVerseCount += ci.verseCount;
                                    adVerseMax += ci.maxVerse;
                                }
                            }
                            break;
                    }
                }
            }
            m_options.otBookCount = otBookCount;
            m_options.ntBookCount = ntBookCount;
            m_options.adBookCount = adBookCount;
            m_options.pBookCount = pBookCount;
            m_options.otChapCount = otChapCount;
            m_options.ntChapCount = ntChapCount;
            m_options.adChapCount = adChapCount;
            m_options.otVerseCount = otVerseCount;
            m_options.ntVerseCount = ntVerseCount;
            m_options.adVerseCount = adVerseCount;
            m_options.otVerseMax = otVerseMax;
            m_options.ntVerseMax = ntVerseMax;
            m_options.adVerseMax = adVerseMax;
        }

    }

}