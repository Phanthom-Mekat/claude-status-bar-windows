namespace ClaudeStatusBar;

/// <summary>Per-session turn bookkeeping fed once per poll: picks a thinking word each time a session ENTERS
/// "thinking" (held while it stays there, never an immediate repeat), and reports the working→done edge
/// for the completion chime, gated on turn length.</summary>
public class TurnTracker
{
    // Claude Code's SPINNER_VERBS minus the hyphenated/tongue-twister ones (same list as macOS).
    public static readonly string[] Words =
    {
        "Accomplishing", "Actioning", "Actualizing", "Architecting", "Baking", "Beaming", "Beboppin'",
        "Befuddling", "Billowing", "Blanching", "Bloviating", "Boogieing", "Boondoggling", "Booping",
        "Bootstrapping", "Brewing", "Bunning", "Burrowing", "Calculating", "Canoodling", "Caramelizing",
        "Cascading", "Catapulting", "Cerebrating", "Channeling", "Channelling", "Churning", "Clauding",
        "Coalescing", "Cogitating", "Combobulating", "Composing", "Computing", "Concocting", "Considering",
        "Contemplating", "Cooking", "Crafting", "Creating", "Crunching", "Crystallizing", "Cultivating",
        "Deciphering", "Deliberating", "Determining", "Doing", "Doodling", "Drizzling", "Ebbing",
        "Effecting", "Elucidating", "Embellishing", "Enchanting", "Envisioning", "Evaporating", "Fermenting",
        "Finagling", "Flambéing", "Flowing", "Flummoxing", "Fluttering", "Forging", "Forming", "Frolicking",
        "Gallivanting", "Galloping", "Garnishing", "Generating", "Gesticulating", "Germinating", "Gitifying",
        "Grooving", "Gusting", "Harmonizing", "Hashing", "Hatching", "Herding", "Honking", "Hullaballooing",
        "Hyperspacing", "Ideating", "Imagining", "Improvising", "Incubating", "Inferring", "Infusing",
        "Ionizing", "Jitterbugging", "Julienning", "Kneading", "Leavening", "Levitating", "Lollygagging",
        "Manifesting", "Marinating", "Meandering", "Metamorphosing", "Misting", "Moonwalking", "Moseying",
        "Mulling", "Mustering", "Musing", "Nebulizing", "Nesting", "Noodling", "Nucleating", "Orbiting",
        "Orchestrating", "Osmosing", "Perambulating", "Percolating", "Perusing", "Pollinating", "Pondering",
        "Pontificating", "Pouncing", "Precipitating", "Processing", "Proofing", "Propagating", "Puttering",
        "Puzzling", "Quantumizing", "Razzmatazzing", "Reticulating", "Roosting", "Ruminating", "Sautéing",
        "Scampering", "Schlepping", "Scurrying", "Seasoning", "Shenaniganing", "Shimmying", "Simmering",
        "Skedaddling", "Sketching", "Slithering", "Smooshing", "Spelunking", "Spinning", "Sprouting",
        "Stewing", "Sublimating", "Swirling", "Swooping", "Symbioting", "Synthesizing", "Tempering",
        "Thinking", "Thundering", "Tinkering", "Tomfoolering", "Transfiguring", "Transmuting", "Twisting",
        "Undulating", "Unfurling", "Unravelling", "Vibing", "Waddling", "Wandering", "Warping",
        "Whirlpooling", "Whirring", "Whisking", "Wibbling", "Working", "Wrangling", "Zesting", "Zigzagging",
    };

    readonly Dictionary<string, string> _prev = new(), _word = new();
    readonly Dictionary<string, long> _turnStart = new();
    readonly Random _rng;

    public TurnTracker(Random? rng = null) { _rng = rng ?? new Random(); }

    /// <summary>Turns that finished (working→done) in the last <see cref="Update"/>: session id + length in seconds.
    /// Reported regardless of the sound threshold (drives the pill's "Done" moment).</summary>
    public List<(string sid, long secs)> Finished { get; } = new();

    /// <summary>The session's current thinking word (e.g. "Noodling"), or "" if it isn't thinking.</summary>
    public string Word(string sid) => _word.TryGetValue(sid, out var w) ? w : "";

    /// <summary>Feed every session's EFFECTIVE state once per poll. Returns true when any session just went
    /// working→done after a turn of at least <paramref name="thresholdSec"/> seconds (0 = sound off).</summary>
    public bool Update(IEnumerable<StatusState> sessions, double thresholdSec, long now)
    {
        bool chime = false;
        Finished.Clear();
        var seen = new HashSet<string>();
        foreach (var s in sessions)
        {
            var id = s.SessionId;
            seen.Add(id);
            var prev = _prev.TryGetValue(id, out var p) ? p : "";

            if (s.State == "thinking" && prev != "thinking")
            {
                var w = Words[_rng.Next(Words.Length)];
                while (_word.TryGetValue(id, out var old) && w == old) w = Words[_rng.Next(Words.Length)];
                _word[id] = w;
            }

            if (s.State is "thinking" or "tool" && s.StartedAt > 0) _turnStart[id] = s.StartedAt;
            if (s.State == "done" && prev != "done" && _turnStart.TryGetValue(id, out var began) && began > 0)
                Finished.Add((id, Math.Max(0, now - began)));
            if (thresholdSec > 0 && s.State == "done" && prev != "done"
                && _turnStart.TryGetValue(id, out var st) && st > 0
                && (thresholdSec < 1 || now - st >= thresholdSec)) chime = true; // <1 = "Every turn" (ts are whole seconds)
            if (s.State == "done") _turnStart[id] = 0;

            _prev[id] = s.State;
        }
        foreach (var gone in _prev.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _prev.Remove(gone); _word.Remove(gone); _turnStart.Remove(gone);
        }
        return chime;
    }
}
