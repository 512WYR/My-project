using UnityEngine;

namespace Ruin.Runtime
{
    // Two looping stems: exploration for the route and every room page, battle for the fight page.
    // StageOneApp.AwaitingNode is false exactly while the battle page owns the screen, so the director
    // watches that one flag instead of every page learning about music. Both sources are kept alive and
    // paused at silence, so walking back into the route resumes the exploration track where it stopped.
    public sealed class MusicDirector : MonoBehaviour
    {
        public const string ExplorationResource = "StageOne/Audio/exploration";
        public const string BattleResource = "StageOne/Audio/battle";

        // The battle master is much hotter than the exploration one, so it gets a lower base volume to
        // keep page changes from jumping the loudness. Fade is unscaled: music should not stop on pause.
        private const float ExplorationVolume = .85f;
        private const float BattleVolume = .4f;
        private const float FadeSeconds = 1.2f;

        private sealed class Track
        {
            public AudioSource Source;
            public float BaseVolume;
            public float Level;
            public bool Paused;
        }

        private StageOneApp source;
        private Track exploration;
        private Track battle;
        private bool battleWanted;

        public static MusicDirector Ensure()
        {
            var existing = FindObjectOfType<MusicDirector>();
            if (existing) return existing;
            var director = new GameObject("MusicDirector").AddComponent<MusicDirector>();
            director.Build();
            return director;
        }

        private void Build()
        {
            exploration = MakeTrack("Exploration Music", ExplorationResource, ExplorationVolume);
            battle = MakeTrack("Battle Music", BattleResource, BattleVolume);
        }

        private Track MakeTrack(string name, string path, float baseVolume)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (!clip)
            {
                // A missing stem must not take the whole game down; the other track still plays.
                Debug.LogWarning("Music clip missing: Resources/" + path);
                return null;
            }

            var audio = gameObject.AddComponent<AudioSource>();
            audio.clip = clip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = 0f;
            return new Track { Source = audio, BaseVolume = baseVolume };
        }

        // StageOneApp owns the game state; binding it here keeps module preview scenes that instantiate a
        // different app (or none) from inheriting music by accident.
        public void Bind(StageOneApp app)
        {
            if (source) source.Changed -= Sync;
            source = app;
            if (!source) return;
            source.Changed += Sync;
            Sync();
        }

        private void Sync()
        {
            // AwaitingNode starts false before the first run exists, so require a live run before calling
            // the screen a battle; otherwise a missing catalog would start the combat track.
            if (source) battleWanted = source.Run != null && !source.AwaitingNode;
        }

        private void OnDestroy()
        {
            if (source) source.Changed -= Sync;
        }

        private void Update()
        {
            // The scene is built from code, so the director can be created before the app has woken up.
            // A late bind is harmless: it only has to observe, never write, the game state.
            if (!source)
            {
                source = FindObjectOfType<StageOneApp>();
                if (source) { source.Changed += Sync; Sync(); }
            }

            var step = Time.unscaledDeltaTime / FadeSeconds;
            Fade(exploration, battleWanted ? 0f : 1f, step);
            Fade(battle, battleWanted ? 1f : 0f, step);
        }

        private static void Fade(Track track, float target, float step)
        {
            if (track == null) return;

            if (target > 0f)
            {
                if (track.Paused) { track.Source.UnPause(); track.Paused = false; }
                else if (!track.Source.isPlaying) track.Source.Play();
            }

            track.Level = Mathf.MoveTowards(track.Level, target, step);
            track.Source.volume = track.BaseVolume * track.Level;

            // Pause at true silence rather than stop: Unity keeps the playback position, so the next
            // exploration stretch continues instead of restarting the loop from the top.
            if (target <= 0f && track.Level <= 0f && track.Source.isPlaying)
            {
                track.Source.Pause();
                track.Paused = true;
            }
        }
    }
}
