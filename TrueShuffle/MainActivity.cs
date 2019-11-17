using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
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
        private SpotifyWebAPI _api;
        private int _currentIndex;
        private bool _notShuffling = true;
        private string _userId;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            string token = GetSharedPreferences("SPOTIFY", 0).GetString("token", "");
            if (token != "")
            {
                _api = new SpotifyWebAPI {AccessToken = token, TokenType = "Bearer"};

                _userId = _api.GetPrivateProfile().Id;
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
        }

        protected override void OnResume()
        {
            base.OnResume();

            LoadPlaylists(0);
        }

        private void LoadPlaylists(int index)
        {
            _currentIndex = index;

            Paging<SimplePlaylist> playlists = _api.GetUserPlaylists(_userId, CountPerPage, index * CountPerPage);

            GridLayout parentLayout = FindViewById<GridLayout>(Resource.Id.playlist_display);
            for (int i = 0; i < 10; i++)
                if (i < playlists.Items.Count)
                {
                    TextView text = (TextView) parentLayout.GetChildAt(i * 2);
                    Button button = (Button) parentLayout.GetChildAt(i * 2 + 1);

                    text.Text = playlists.Items[i].Name;
                    int j = i;
                    button.Click += (sender, e) => OnShuffleButtonClick(playlists.Items[j]);

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

        private async void OnShuffleButtonClick(SimplePlaylist playlist)
        {
            if (!_notShuffling) return;

            _notShuffling = false;

            Task<Exception> task = new Task<Exception>(() =>
            {
                int[] startIndices = Enumerable.Range(0, (int) Math.Ceiling(playlist.Tracks.Total / 100F)).ToArray();
                List<Paging<PlaylistTrack>> playlistTracks =
                    startIndices.Select(i => _api.GetPlaylistTracks(playlist.Id, offset: i * 100)).ToList();

                List<FullTrack> tracks = new List<FullTrack>();
                playlistTracks.ToList().ForEach(playlistTracksGroup =>
                    tracks.AddRange(playlistTracksGroup.Items.Select(playlistTrack => playlistTrack.Track)));

                if (tracks.Count != playlist.Tracks.Total) return new Exception("Failed to get all tracks in playlist");

                Random rnd = new Random();
                List<FullTrack> shuffledTracks = tracks.OrderBy(x => rnd.NextDouble()).ToList();

                List<ErrorResponse> addResult = startIndices.Select(i =>
                        _api.AddPlaylistTracks(playlist.Id,
                            shuffledTracks.Skip(i * 100).Take(100).Select(track => track.Uri).ToList()))
                    .ToList();

                if (addResult.Any(error => error.HasError()))
                    return new Exception("Failed to add all tracks to playlist");

                List<ErrorResponse> removeResult = startIndices.Select(i => _api.RemovePlaylistTracks(playlist.Id,
                    tracks.Skip(i * 100).Take(100)
                        .Select((track, trackIndex) => new DeleteTrackUri(track.Uri, trackIndex)).ToList())).ToList();

                if (removeResult.Any(error => error.HasError()))
                    return new Exception("Failed to remove all tracks from playlist");

                return null;
            });

            task.Start();

            await task.ContinueWith(errorTask =>
            {
                string resultText = errorTask.Result == null ? "Task succeeded" : $"Task failed: {errorTask.Result}";
                Log.Debug("Shuffle", $"Task has completed and {resultText}");

                _notShuffling = true;
            });
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
    }
}