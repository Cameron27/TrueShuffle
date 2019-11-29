using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Util;
using Android.Views;
using Android.Widget;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;
using Xamarin.Essentials;

namespace TrueShuffle
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme")]
    public class MainActivity : AppCompatActivity
    {
        private const int CountPerPage = 10;
        private readonly ValueListener<State> _state = new ValueListener<State> {Value = State.Waiting};
        private readonly object _stateChangeLock = new object();
        private SpotifyWebAPI _api;
        private int _currentIndex;
        private string _lastError;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            string token = GetSharedPreferences("SPOTIFY", 0).GetString("token", "");
            if (token != "")
            {
                _api = new SpotifyWebAPI { AccessToken = token, TokenType = "Bearer" };
            }
            else
            {
                Intent i = new Intent(this, typeof(LoginActivity));
                i.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                StartActivity(i);
            }

            Button nextButton = FindViewById<Button>(Resource.Id.next_button);
            Button previousButton = FindViewById<Button>(Resource.Id.previous_button);
            nextButton.Click += OnNavigationButtonClick;
            previousButton.Click += OnNavigationButtonClick;

            _state.Action = OnStateChange;
        }

        protected override void OnResume()
        {
            base.OnResume();

            LoadPlaylists(0);
        }

        private void LoadPlaylists(int index)
        {
            _currentIndex = index;

            Paging<SimplePlaylist> playlists;
            try
            {
                string id = _api.GetPrivateProfile().Id;
                playlists = _api.GetUserPlaylists(id, CountPerPage, index * CountPerPage);
            }
            catch (Exception e)
            {
                TextView statusTextView = FindViewById<TextView>(Resource.Id.status_text_view);
                statusTextView.Text = "Failed to load playlists";

                return;
            }

            GridLayout parentLayout = FindViewById<GridLayout>(Resource.Id.playlist_display);
            for (int i = 0; i < 10; i++)
                if (i < playlists.Items.Count)
                {
                    TextView text = (TextView) parentLayout.GetChildAt(i * 2);
                    Button button = (Button) parentLayout.GetChildAt(i * 2 + 1);

                    text.Text = playlists.Items[i].Name;
                    int j = i;
                    button.Click += (sender, e) => OnShuffleButtonClick(playlists.Items[j].Id);

                    text.Visibility = ViewStates.Visible;
                    button.Visibility = ViewStates.Visible;
                }
                else
                {
                    parentLayout.GetChildAt(i * 2).Visibility = ViewStates.Gone;
                    parentLayout.GetChildAt(i * 2 + 1).Visibility = ViewStates.Gone;
                }

            Button nextButton = FindViewById<Button>(Resource.Id.next_button);
            nextButton.Visibility = playlists.HasNextPage() ? ViewStates.Visible : ViewStates.Invisible;

            Button previousButton = FindViewById<Button>(Resource.Id.previous_button);
            previousButton.Visibility = playlists.HasPreviousPage() ? ViewStates.Visible : ViewStates.Invisible;
        }

        private async void OnShuffleButtonClick(string playlistId)
        {
            if (_state.Value != State.Waiting && _state.Value != State.Failed) return;

            _state.Value = State.LoadingTracks;

            Task<Exception> task = Task<Exception>.Factory.StartNew(() =>
            {
                try
                {
                    // get playlist
                    FullPlaylist playlist =
                        _api.GetPlaylist(playlistId, fields: "id,tracks.items(track.uri),tracks.total");

                    if (playlist.Tracks == null)
                        return new Exception("Failed to get playlist");

                    // get tracks
                    int[] startIndices =
                        Enumerable.Range(0, (int) Math.Ceiling(playlist.Tracks.Total / 100F)).ToArray();
                    List<Paging<PlaylistTrack>> playlistTracks = new List<Paging<PlaylistTrack>> {playlist.Tracks};
                    playlistTracks.AddRange(startIndices.Skip(1)
                        .Select(i => _api.GetPlaylistTracks(playlist.Id, fields: "items(track.uri)", offset: i * 100))
                        .ToList());

                    // add tracks to single list
                    List<FullTrack> tracks = new List<FullTrack>();
                    playlistTracks.ToList().ForEach(playlistTracksGroup =>
                        tracks.AddRange(playlistTracksGroup.Items.Select(playlistTrack => playlistTrack.Track)));

                    if (tracks.Count != playlist.Tracks.Total)
                        return new Exception("Failed to get all tracks in playlist");

                    // randomize track order
                    Random rnd = new Random();
                    List<FullTrack> shuffledTracks = tracks.OrderBy(x => rnd.NextDouble()).ToList();

                    // add tracks
                    _state.Value = State.AddingTracks;
                    List<ErrorResponse> addResult = startIndices.Select(i =>
                            _api.AddPlaylistTracks(playlist.Id,
                                shuffledTracks.Skip(i * 100).Take(100).Select(track => track.Uri).ToList()))
                        .ToList();

                    if (addResult.Any(error => error.HasError()))
                        return new Exception("Failed to add all tracks to playlist");

                    // delete tracks
                    _state.Value = State.RemovingTracks;
                    List<ErrorResponse> removeResult = startIndices.Select(i => _api.RemovePlaylistTracks(playlist.Id,
                            tracks.Skip(i * 100).Take(100)
                                .Select((track, trackIndex) => new DeleteTrackUri(track.Uri, trackIndex)).ToList()))
                        .ToList();

                    if (removeResult.Any(error => error.HasError()))
                        return new Exception("Failed to remove all tracks from playlist");

                    return null;
                }
                catch (Exception e)
                {
                    return new Exception("Unknown error has occured");
                }
            });

            await task.ContinueWith(errorTask =>
            {
                if (errorTask.Result == null)
                {
                    Log.Debug("ShuffleResult", "Shuffle succeeded");

                    _state.Value = State.Waiting;
                }
                else
                {
                    Log.Debug("ShuffleResult", $"Shuffle failed: {errorTask.Result}");
                    _lastError = errorTask.Result.Message;

                    _state.Value = State.Failed;
                }
            });
        }

        private void OnStateChange(object sender, ValueListenerEventArgs<State> e)
        {
            if (_state.Value == e.OldValue) return;

            RunOnUiThread(OnStateChangeUi);
        }

        private void OnStateChangeUi()
        {
            lock (_stateChangeLock)
            {
                TextView statusTextView = FindViewById<TextView>(Resource.Id.status_text_view);
                switch (_state.Value)
                {
                    case State.LoadingTracks:
                        statusTextView.Text = "Loading Tracks...";
                        break;
                    case State.RemovingTracks:
                        statusTextView.Text = "Removing Old Tracks...";
                        break;
                    case State.AddingTracks:
                        statusTextView.Text = "Adding Shuffled Tracks...";
                        break;
                    case State.Waiting:
                        statusTextView.Text = "";
                        break;
                    case State.Failed:
                        statusTextView.Text = _lastError;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }


                GridLayout parentLayout = FindViewById<GridLayout>(Resource.Id.playlist_display);
                for (int i = 1; i < parentLayout.ChildCount; i += 2)
                {
                    Button button = (Button) parentLayout.GetChildAt(i);
                    button.Activated = _state.Value == State.Waiting || _state.Value == State.Failed;
                    Color color = new Color(GetColor(_state.Value == State.Waiting || _state.Value == State.Failed
                        ? Resource.Color.spotify_green
                        : Resource.Color.spotify_grey));
                    button.SetBackgroundColor(color);
                }
            }
        }

        private void OnNavigationButtonClick(object sender, EventArgs e)
        {
            Button senderButton = (Button) sender;
            int offset = senderButton.Id == Resource.Id.next_button ? 1 : -1;
            LoadPlaylists(_currentIndex + offset);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        private enum State
        {
            LoadingTracks,
            RemovingTracks,
            AddingTracks,
            Waiting,
            Failed
        }
    }
}