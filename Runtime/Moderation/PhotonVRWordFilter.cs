using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Photon.VR
{
    /// <summary>
    /// Blocks offensive words in player names and room codes.
    /// <para>Text is normalised before matching: lower case, accents removed, look-alike letters (Cyrillic, leetspeak
    /// such as 1→i, 3→e, @→a, $→s, !→i) mapped, and everything that is not a letter dropped. Each blocked word matches
    /// with any letter repeated ("fuuuck"), so repeats cannot sneak past it.</para>
    /// List syntax (one entry per line, see Resources/PhotonVR/Filter): <c>word</c> matches anywhere,
    /// <c>=word</c> only as a whole word, <c>raw:1488</c> matches the digits as typed, <c># comment</c>.
    /// Allow-list entries are removed before matching ("assassin", "cockpit").
    /// </summary>
    public sealed class PhotonVRWordFilter
    {
        private readonly List<Regex> anywhere = new List<Regex>();
        private readonly List<Regex> wholeWord = new List<Regex>();
        private readonly List<string> raw = new List<string>();
        private readonly List<string> allowed = new List<string>();

        private static PhotonVRWordFilter defaultFilter;

        // Domain reload can be turned off (Enter Play Mode Options); start every play session clean.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            defaultFilter = null;
        }

        /// <summary>The filter used by PhotonVR. Built from the PhotonVRBlocklist asset (or the default lists) on first use; assign to replace it.</summary>
        public static PhotonVRWordFilter Default
        {
            get => defaultFilter ?? (defaultFilter = PhotonVRBlocklist.BuildFilter());
            set => defaultFilter = value;
        }

        /// <summary>Optional extra rule, e.g. a call to your backend's moderation service. Return true to block.</summary>
        public Func<string, bool> CustomCheck;

        public int Count => anywhere.Count + wholeWord.Count + raw.Count;

        public PhotonVRWordFilter(IEnumerable<string> blockLines, IEnumerable<string> allowLines = null)
        {
            AddBlocked(blockLines);
            AddAllowed(allowLines);
        }

        public void AddBlocked(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (string line in lines)
            {
                string entry = line?.Trim();
                if (string.IsNullOrEmpty(entry) || entry.StartsWith("#")) continue;

                if (entry.StartsWith("raw:", StringComparison.OrdinalIgnoreCase))
                {
                    string digits = entry.Substring(4).Trim().ToLowerInvariant();
                    if (digits.Length > 0) raw.Add(digits);
                    continue;
                }

                bool whole = entry.StartsWith("=");
                string word = Letters(Normalize(whole ? entry.Substring(1) : entry));
                if (word.Length == 0) continue;
                string pattern = string.Concat(word.Select(c => Regex.Escape(c.ToString()) + "+"));
                if (whole) wholeWord.Add(new Regex("^" + pattern + "$", RegexOptions.CultureInvariant));
                else anywhere.Add(new Regex(pattern, RegexOptions.CultureInvariant));
            }
        }

        public void AddAllowed(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (string line in lines)
            {
                string entry = line?.Trim();
                if (string.IsNullOrEmpty(entry) || entry.StartsWith("#")) continue;
                string word = Letters(Normalize(entry));
                if (word.Length > 0) allowed.Add(word);
            }
            allowed.Sort((a, b) => b.Length.CompareTo(a.Length)); // longest first
        }

        /// <summary>True when <paramref name="text"/> contains a blocked word.</summary>
        public bool IsBlocked(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (CustomCheck != null && CustomCheck(text)) return true;

            string lower = text.ToLowerInvariant();
            foreach (string r in raw)
                if (lower.Contains(r) || DigitsOnly(lower).Contains(r)) return true;

            string normalized = Normalize(text);
            string[] tokens = Regex.Split(normalized, "[^a-z]+").Where(t => t.Length > 0).ToArray();
            foreach (string token in tokens)
                foreach (Regex rx in wholeWord)
                    if (rx.IsMatch(token)) return true;

            foreach (string part in Parts(tokens))
            {
                string p = part;
                foreach (string a in allowed)
                    p = p.Replace(a, " ");
                foreach (string piece in p.Split(' '))
                    foreach (Regex rx in anywhere)
                        if (rx.IsMatch(piece)) return true;
            }
            return false;
        }

        // Each word on its own, plus runs of 1-2 letter fragments joined ("f u c k", "fu.ck").
        private static IEnumerable<string> Parts(string[] tokens)
        {
            var run = new StringBuilder();
            foreach (string t in tokens)
            {
                yield return t;
                if (t.Length <= 2) run.Append(t);
                else
                {
                    if (run.Length > 2) yield return run.ToString();
                    run.Clear();
                }
            }
            if (run.Length > 2) yield return run.ToString();
        }

        /// <summary>Room codes: blocked words, plus only letters, digits, '-' and '_' allowed (max 32 characters).</summary>
        public bool IsRoomCodeAllowed(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 32) return false;
            foreach (char c in code)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            return !IsBlocked(code);
        }

        /// <summary>Returns a safe name to display: rich-text and control characters removed, blocked names replaced by "Player{actor}".</summary>
        public string SanitizeName(string name, int actor)
        {
            string clean = StripMarkup(name);
            if (clean.Length > PhotonVRProfiles.MaxNameLength) clean = clean.Substring(0, PhotonVRProfiles.MaxNameLength);
            return clean.Trim().Length == 0 || IsBlocked(clean) ? "Player" + actor : clean;
        }

        private static readonly Regex MarkupTag = new Regex(@"</?[a-zA-Z#][^<>]{0,64}>", RegexOptions.Compiled);

        public static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = MarkupTag.Replace(text, string.Empty);
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                if (!char.IsControl(c) && c != '<' && c != '>') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Lower case, accents stripped, look-alikes and leetspeak mapped to letters.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string decomposed;
            try { decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { decomposed = text.ToLowerInvariant(); }

            var sb = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(Map(c));
            }
            return sb.ToString();
        }

        private static char Map(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': case '!': case '|': case 'í': case 'ı': return 'i';
                case '3': return 'e';
                case '4': case '@': return 'a';
                case '5': case '$': return 's';
                case '6': case '9': return 'g';
                case '7': case '+': return 't';
                case '8': return 'b';
                case '2': return 'z';
                case '¢': case '(': return 'c';
                // Cyrillic / Greek look-alikes
                case 'а': return 'a';
                case 'в': return 'b';
                case 'е': case 'ё': return 'e';
                case 'к': return 'k';
                case 'м': return 'm';
                case 'н': return 'h';
                case 'о': case 'ο': return 'o';
                case 'р': case 'ρ': return 'p';
                case 'с': return 'c';
                case 'т': return 't';
                case 'у': return 'y';
                case 'х': return 'x';
                case 'і': case 'ї': case 'ι': return 'i';
                case 'ѕ': return 's';
                case 'ν': return 'v';
                default: return c;
            }
        }

        private static string Letters(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (c >= 'a' && c <= 'z') sb.Append(c);
            return sb.ToString();
        }

        private static string DigitsOnly(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
                if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }
    }
}
