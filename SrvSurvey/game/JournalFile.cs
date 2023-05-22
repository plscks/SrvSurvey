﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.game;
using System.Reflection;

namespace SrvSurvey
{
    class JournalFile
    {

        public static string journalFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Saved Games\Frontier Developments\Elite Dangerous\");

        private static readonly Dictionary<string, Type> typeMap;

        static JournalFile()
        {
            // build a map of all types derived from JournalEntry
            typeMap = new Dictionary<string, Type>();

            var journalEntryType = typeof(JournalEntry);
            var journalDerivedTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(_ => journalEntryType.IsAssignableFrom(_));

            foreach (var journalType in journalDerivedTypes)
                typeMap.Add(journalType.Name, journalType);
        }

        public List<JournalEntry> Entries { get; } = new List<JournalEntry>();

        protected StreamReader reader;
        public readonly string filepath;
        public readonly DateTime timestamp;
        public readonly string? CommanderName;
        public readonly bool isOdyssey;

        public JournalFile(string filepath)
        {
            Game.log($"Reading: {Path.GetFileName(filepath)}");

            this.filepath = filepath;
            this.timestamp = File.GetLastWriteTime(filepath);

            this.reader = new StreamReader(new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

            this.readEntries();

            var entry = this.FindEntryByType<Commander>(0, false);
            this.CommanderName = entry?.Name;

            if (this.Entries.Count > 0)
                this.isOdyssey = ((Fileheader)this.Entries[0]).Odyssey;
        }

        public int Count { get => this.Entries.Count; }

        public void readEntries()
        {
            while (!this.reader.EndOfStream)
            {
                this.readEntry();
            }
        }

        protected virtual JournalEntry? readEntry()
        {
            // read next entry, add to list or skip if it's blank
            var entry = this.parseNextEntry();

            if (entry != null)
                this.Entries.Add(entry);

            return entry;
        }

        private JournalEntry? parseNextEntry()
        {
            var json = reader.ReadLine()!;
            JToken entry = JsonConvert.DeserializeObject<JToken>(json)!;
            if (entry == null) return null;

            var eventName = entry["event"]!.Value<string>()!;
            if (typeMap.ContainsKey(eventName))
            {
                return entry.ToObject(typeMap[eventName]) as JournalEntry;
            }

            // ignore anything else
            return null;
        }

        public T? FindEntryByType<T>(int index, bool searchUp) where T : JournalEntry
        {
            if (index == -1) index = this.Entries.Count - 1;

            int n = index;
            while (n >= 0 && n < this.Entries.Count)
            {
                if (this.Entries[n].GetType() == typeof(T))
                {
                    return this.Entries[n] as T;
                }
                n += searchUp ? -1 : +1;
            }

            return null;
        }

        public bool search<T>(Func<T, bool> func) where T : JournalEntry
        {
            int idx = this.Count - 1;
            do
            {
                var entry = this.FindEntryByType<T>(idx, true);
                if (entry == null)
                {
                    // no more entries in this file
                    break;
                }

                // do something with the entry, exit if finished
                var finished = func(entry);
                if (finished) return finished;

                // otherwise keep going
                idx = this.Entries.IndexOf(entry) - 1;
            } while (idx >= 0);

            // if we run out of entries, we don't know if we're necessarily finished
            return false;
        }

        public void searchDeep<T>(Func<T, bool> func, Func<JournalFile, bool>? finishWhen = null) where T : JournalEntry
        {
            var count = 0;
            var journals = this;

            // search older journals
            while (journals != null)
            {
                ++count;
                // search journals
                var finished = journals.search(func);
                if (finished) break;

                if (finishWhen != null)
                {
                    finished = finishWhen(journals);
                    if (finished) break;
                }

                var priorFilepath = JournalFile.getCommanderJournalBefore(this.CommanderName, this.isOdyssey, journals.timestamp);
                journals = priorFilepath == null ? null : new JournalFile(priorFilepath);
            };

            Game.log($"searchJournalsDeep: count: {count}");
        }

        public void walk(int index, bool searchUp, Func<JournalEntry, bool> func)
        {
            int idx = index;
            if (idx == -1)
                idx = this.Count - 1;

            // the end is either the first or last element
            var endIdx = searchUp ? 0 : this.Count - 1;
            
            while (idx != endIdx)
            {
                var finished = func(this.Entries[idx]);
                if (finished) return;

                // increment index and go round again
                if (searchUp)
                    idx--;
                else
                    idx++;
            } 
        }

        public static string? getCommanderJournalBefore(string? cmdr, bool isOdyssey, DateTime timestamp)
        {
            var manyFiles = new DirectoryInfo(JournalFile.journalFolder)
                .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(_ => _.LastWriteTimeUtc);

            var journalFiles = manyFiles
                .Where(_ => _.LastWriteTime < timestamp)
                .Select(_ => _.FullName);

            if (journalFiles.Count() == 0) return null;

            if (string.IsNullOrWhiteSpace(cmdr))
            {
                // use the most recent journal file
                return journalFiles.First();
            }

            var CMDR = cmdr.ToUpper();

            var filename = journalFiles.FirstOrDefault((filepath) =>
            {
                using (var reader = new StreamReader(new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();

                        if (line == null) break;

                        if (line.Contains("\"event\":\"Fileheader\"") && !line.ToUpperInvariant().Contains($"\"Odyssey\":{isOdyssey}".ToUpperInvariant()))
                                return false;

                        if (line.Contains("\"event\":\"Commander\""))
                            // no need to process further lines
                            return line.ToUpper().Contains($"\"NAME\":\"{CMDR}\"");
                    }
                    return false;
                }
            });

            // TODO: As we already loaded the journal into memory, it would be nice to use that rather than reload it again from JournalWatcher
            return filename;
        }
    }
}
