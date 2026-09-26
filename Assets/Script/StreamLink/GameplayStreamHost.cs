using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Logging;
using YARG.Core.StreamLink;
using YARG.Gameplay;
using YARG.Gameplay.Player;

namespace YARG.StreamLink
{
    /// <summary>
    /// Connects the <see cref="StreamDirector"/> to the song being played. Outside a song every command
    /// is rejected as not_in_song.
    /// </summary>
    public sealed class GameplayStreamHost : IStreamGameHost
    {
        // Keeps a changed note from appearing half-drawn at the far end of the highway
        private const double SPAWN_MARGIN = 0.25;

        public StreamLinkClient Client { get; set; }

        private GameManager          _game;
        private FiveFretGuitarPlayer _player;
        private List<GuitarNote>     _pristine;
        private bool                 _detached = true;

        public bool IsInSong => !_detached && _game != null;

        public void Attach(GameManager game)
        {
            _game = game;
            _detached = false;
            _player = null;
            _pristine = null;

            // v1 supports exactly one five-fret guitar or bass player
            if (game.Players.Count == 1 && game.Players[0] is FiveFretGuitarPlayer player &&
                player.Player.Profile.CurrentInstrument is Instrument.FiveFretGuitar or Instrument.FiveFretBass)
            {
                _player = player;
                // Taken before any effect runs, and after the load-time modifiers were applied
                _pristine = ChartMutator.Snapshot(player.LiveNotes);
            }
        }

        public void Detach()
        {
            _detached = true;
            _game = null;
            _player = null;
            _pristine = null;
        }

        public bool IsSupportedInstrument => _player != null;

        // Stream mode off is treated like practice/replays: the highway would jump on speed changes and
        // the results would be saved, so nothing is applied
        public bool IsPracticeOrReplay => !IsInSong || !StreamLinkService.IsStreamModeActive(_game);

        public double SecondsOfNotesRemaining => IsInSong ? _game.LastNoteTime - _game.SongTime : 0;

        public void SetSpeed(double speed)
        {
            if (!IsInSong)
            {
                return;
            }

            _game.SetSongSpeed((float) speed);
            foreach (var player in _game.Players)
            {
                player.BaseEngine.SetSpeed(speed);
            }
        }

        public bool RebuildUpcoming(GuitarNoteType? noteType, int[] fretMap)
        {
            if (!IsInSong || _player == null)
            {
                return true;
            }

            var notes = _player.LiveNotes;
            double minTime = _game.VisualTime + _player.SpawnTimeOffset + SPAWN_MARGIN;

            int from = _player.SpawnIndex;
            while (from < notes.Count && notes[from].Time < minTime)
            {
                from++;
            }

            if (from >= notes.Count)
            {
                // Nothing left that hasn't spawned
                return true;
            }

            if (from <= _player.Engine.NoteIndex)
            {
                YargLogger.LogFormatWarning("[StreamLink] Rebuild boundary {0} is not past the engine's note {1}, retrying",
                    from, _player.Engine.NoteIndex);
                return false;
            }

            ChartMutator.Rebuild(notes, _pristine, from, noteType, fretMap);
            return true;
        }

        public void SendAck(StreamAck ack)
        {
            YargLogger.LogFormatDebug("[StreamLink] {0}", $"{ack.StatusName} {ack.Mod} {ack.Reason} ({ack.CommandId})");
            Client?.SendAck(ack);
        }
    }
}
