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
using AlertDialog = Android.App.AlertDialog;

namespace TrueShuffle
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme")]
    public class MainActivity : AppCompatActivity
    {
        private const int CountPerPage = 8;
        private readonly EventHandler[] _buttonEvents = new EventHandler[CountPerPage];
        private readonly ValueListener<State> _state = new ValueListener<State> {Value = State.Waiting};
        private readonly object _stateChangeLock = new object();
        private SpotifyWebAPI _api;
        private int _currentIndex;
        private string _lastError;
        private List<SimplePlaylist> _playlists;
        private const int ArtistLimit = 10;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            string token = GetSharedPreferences("SPOTIFY", 0).GetString("token", "");
            if (token != "")
            {
                _api = new SpotifyWebAPI {AccessToken = token, TokenType = "Bearer"};
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

            // get playlists
            if (_playlists == null)
            {
                _playlists = new List<SimplePlaylist>();
                try
                {
                    string id = _api.GetPrivateProfile().Id;
                    Paging<SimplePlaylist> subPlaylists;
                    int i = 0;
                    do
                    {
                        subPlaylists = _api.GetUserPlaylists(id, CountPerPage, i++ * CountPerPage);
                        _playlists.AddRange(subPlaylists.Items);
                    } while (subPlaylists.HasNextPage());
                }
                catch (Exception)
                {
                    TextView statusTextView = FindViewById<TextView>(Resource.Id.status_text_view);
                    statusTextView.Text = "Failed to load playlists";
                    
                    return;
                }
            }

            // filter playlists
            List<SimplePlaylist> displayPlaylists = _playlists
                .Where(p =>
                    !(p.Name.EndsWith(" Shuffle") &&
                      _playlists.Any(p2 => p2.Name == p.Name.Substring(0, p.Name.Length - 8))) &&
                    !(p.Name.EndsWith(" Restrict") &&
                      _playlists.Any(p2 => p2.Name == p.Name.Substring(0, p.Name.Length - 9))))
                .ToList();

            // display playlists
            GridLayout parentLayout = FindViewById<GridLayout>(Resource.Id.playlist_display);
            for (int i = _currentIndex * CountPerPage; i < (_currentIndex + 1) * CountPerPage; i++)
                if (i < displayPlaylists.Count)
                {
                    TextView text = (TextView) parentLayout.GetChildAt(i % CountPerPage * 2);
                    Button button = (Button) parentLayout.GetChildAt(i % CountPerPage * 2 + 1);

                    text.Text = displayPlaylists[i].Name;
                    string id = displayPlaylists[i].Id;

                    if (_buttonEvents[i % CountPerPage] != null)
                        button.Click -= _buttonEvents[i % CountPerPage];

                    void Event(object sender, EventArgs e)
                    {
                        OnSelectButtonClick(id);
                    }

                    button.Click += Event;
                    _buttonEvents[i % CountPerPage] = Event;

                    text.Visibility = ViewStates.Visible;
                    button.Visibility = ViewStates.Visible;
                }
                else
                {
                    parentLayout.GetChildAt(i % CountPerPage * 2).Visibility = ViewStates.Gone;
                    parentLayout.GetChildAt(i % CountPerPage * 2 + 1).Visibility = ViewStates.Gone;
                }

            // show or hind nav buttons
            Button nextButton = FindViewById<Button>(Resource.Id.next_button);
            nextButton.Visibility = (_currentIndex + 1) * CountPerPage < _playlists.Count
                ? ViewStates.Visible
                : ViewStates.Invisible;

            Button previousButton = FindViewById<Button>(Resource.Id.previous_button);
            previousButton.Visibility = _currentIndex > 0 ? ViewStates.Visible : ViewStates.Invisible;
        }

        private void OnSelectButtonClick(string playlistId)
        {
            AlertDialog.Builder dialogBuilder = new AlertDialog.Builder(this, Resource.Style.DialogTheme);
            LayoutInflater inflater = (LayoutInflater) GetSystemService(LayoutInflaterService);
            View popupView = inflater.Inflate(Resource.Layout.popup, null);

            dialogBuilder.SetView(popupView);
            AlertDialog dialog = dialogBuilder.Create();
            dialog.Show();

            popupView.FindViewById<Button>(Resource.Id.shuffle_button).Click += (sender, e) =>
            {
                dialog.Dismiss();
                OnShuffleButtonClick(playlistId, ShuffleMode.Shuffle, popupView);
            };
            popupView.FindViewById<Button>(Resource.Id.restrict_button).Click += (sender, e) =>
            {
                dialog.Dismiss();
                OnShuffleButtonClick(playlistId, ShuffleMode.Restrict, popupView);
            };
            popupView.FindViewById<EditText>(Resource.Id.restrict_value).Text =
                GetSharedPreferences("SPOTIFY", 0).GetString("RESTRICT_VALUE", "10");
        }

        private async void OnShuffleButtonClick(string playlistId, ShuffleMode shuffleMode, View popup)
        {
            if (_state.Value != State.Waiting && _state.Value != State.Failed) return;

            _state.Value = State.LoadingTracks;

            Task<Exception> task = Task<Exception>.Factory.StartNew(() =>
            {
                try
                {
                    // get playlist
                    FullPlaylist playlist =
                        _api.GetPlaylist(playlistId, fields: "id,name,tracks.items(track.uri),tracks.total");

                    if (playlist.Tracks == null)
                        return new Exception("Failed to get playlist");

                    // get tracks
                    IList<FullTrack> tracks = Enumerable.Range(0, (int) Math.Ceiling(playlist.Tracks.Total / 100F))
                        .Select(i =>
                            _api.GetPlaylistTracks(playlist.Id, fields: "items.track(uri,artists.name)",
                                offset: i * 100))
                        .SelectMany(group => group.Items.Select(playlistTrack => playlistTrack.Track))
                        .ToList();

                    if (tracks.Count != playlist.Tracks.Total)
                        return new Exception("Failed to get all tracks in playlist");

                    // randomize track order
                    if (shuffleMode == ShuffleMode.Shuffle)
                        tracks.Shuffle();
                    else if (shuffleMode == ShuffleMode.Restrict)
                    {
                        // get value
                        EditText value = popup.FindViewById<EditText>(Resource.Id.restrict_value);
                        bool res = int.TryParse(value.Text, out int artistLimit);

                        // check value is valid
                        if(!res || artistLimit < 1) throw new Exception("Restrict value is not a positive integer");
                        
                        // save value
                        ISharedPreferencesEditor editor = GetSharedPreferences("SPOTIFY", 0).Edit();
                        editor.PutString("RESTRICT_VALUE", artistLimit.ToString());
                        editor.Commit();
                        
                        tracks = tracks
                            .GroupBy(track => track.Artists[0].Name)
                            .SelectMany(artist => artist.ToList().Shuffle().Take(artistLimit))
                            .ToList()
                            .Shuffle();
                    }

                    // add tracks
                    string id = _api.GetPrivateProfile().Id;

                    // delete old playlists
                    foreach (SimplePlaylist p in _playlists.Where(p => p.Name == playlist.Name + " " + shuffleMode)
                        .ToList())
                    {
                        ErrorResponse error = _api.UnfollowPlaylist(id, p.Id);
                        if (error.HasError())
                            return new Exception("Failed to delete old playlist");
                        _playlists.Remove(p);
                    }

                    // create new playlist
                    _state.Value = State.AddingTracks;
                    FullPlaylist newPlaylist = _api.CreatePlaylist(id, playlist.Name + " " + shuffleMode, false);
                    if (newPlaylist.Id == null)
                        return new Exception("Failed to create new playlist");


                    _playlists.Add(new SimplePlaylist {Id = newPlaylist.Id, Name = newPlaylist.Name});

                    // add tracks
                    List<ErrorResponse> addResult = Enumerable.Range(0, (int) Math.Ceiling(tracks.Count / 100F)).Select(
                            i =>
                                _api.AddPlaylistTracks(newPlaylist.Id,
                                    tracks.Skip(i * 100).Take(100).Select(track => track.Uri).ToList()))
                        .ToList();

                    if (addResult.Any(error => error.HasError()))
                        return new Exception("Failed to add all tracks to playlist");

                    return null;
                }
                catch (Exception)
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

        private enum ShuffleMode
        {
            Shuffle,
            Restrict
        }
    }
}