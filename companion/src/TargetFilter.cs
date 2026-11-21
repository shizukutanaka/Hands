using System;

namespace HandsCompanion
{
    /* Pure decision logic for the profile's optional target filter.
       Deliberately free of any OS calls so it can be unit-tested
       anywhere: the caller (Program) supplies the foreground window's
       title and process name, obtained through WindowsSender, and this
       class only decides.

       Semantics (mirrored by the tests):
       - target == null          -> always match (historical behaviour:
                                    keys go to whatever window has focus)
       - titleContains set       -> the foreground title must contain it,
                                    case-insensitively
       - processName set         -> the foreground process name must equal
                                    it, case-insensitively, ignoring a
                                    trailing ".exe" on either side
       - both set                -> both must hold (AND)

       The filter only ever narrows where keys are sent. It grants no
       access to another process: the foreground title/process are read
       through the same window APIs the sender already used to report
       refused sends. */
    public static class TargetFilter
    {
        public static string StripExe(string name)
        {
            if (name == null) return null;
            string n = name.Trim();
            if (n.Length >= 4 && n.Substring(n.Length - 4).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            return n;
        }

        public static bool Matches(ProfileTarget target, string foregroundTitle, string foregroundProcess)
        {
            if (target == null) return true;
            if (target.TitleContains != null)
            {
                if (string.IsNullOrEmpty(foregroundTitle)) return false;
                if (foregroundTitle.IndexOf(target.TitleContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            if (target.ProcessName != null)
            {
                if (string.IsNullOrEmpty(foregroundProcess)) return false;
                if (!string.Equals(StripExe(target.ProcessName), StripExe(foregroundProcess), StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        public static string Describe(ProfileTarget target)
        {
            if (target == null) return "(no target filter)";
            var parts = new System.Collections.Generic.List<string>();
            if (target.TitleContains != null) parts.Add("window title contains \"" + target.TitleContains + "\"");
            if (target.ProcessName != null) parts.Add("process is \"" + target.ProcessName + "\"");
            return string.Join(" and ", parts.ToArray());
        }

        /* Human-readable reason a send was skipped, for the log: a
           filtered-out send is surfaced, never silently dropped. */
        public static string MismatchReason(ProfileTarget target, string foregroundTitle, string foregroundProcess)
        {
            string fg = "\"" + (string.IsNullOrEmpty(foregroundTitle) ? "(untitled window)" : foregroundTitle) + "\"" +
                (string.IsNullOrEmpty(foregroundProcess) ? "" : " (process " + foregroundProcess + ")");
            return "foreground window is " + fg + ", target requires " + Describe(target) + "; not sending";
        }
    }
}
