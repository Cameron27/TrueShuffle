using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Models;
using Xamarin.Essentials;
using ContextThemeWrapper = Android.Support.V7.View.ContextThemeWrapper;

namespace TrueShuffle
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme")]
    public class MainActivity : AppCompatActivity
    {
        private SpotifyWebAPI api;
        private string userId;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            string token = GetSharedPreferences("SPOTIFY", 0).GetString("token", "");
            if (token != "")
            {
                api = new SpotifyWebAPI {AccessToken = token, TokenType = "Bearer"};

                userId = api.GetPrivateProfile().Id;
            }
            else
            {
                Intent i = new Intent(this, typeof(LoginActivity));
                i.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                StartActivity(i);
            }
        }

        protected override void OnResume()
        {
            base.OnResume();

            LoadPlaylists();
        }

        private void LoadPlaylists()
        {
            Paging<SimplePlaylist> playlists = api.GetUserPlaylists(userId, 10);

            GridLayout parentLayout = FindViewById<GridLayout>(Resource.Id.playlist_display);
            foreach (SimplePlaylist playlist in playlists.Items)
            {
                TextView textView = new TextView(new ContextThemeWrapper(this, Resource.Style.PlaylistLabel))
                {
                    Text = playlist.Name,
                    LayoutParameters = new GridLayout.LayoutParams(GridLayout.InvokeSpec(GridLayout.Undefined, 0f),
                        GridLayout.InvokeSpec(GridLayout.Undefined, 1f))
                };
                textView.LayoutParameters.Width = 0;

                Button button = new Button(new ContextThemeWrapper(this, Resource.Style.PlaylistButton))
                {
                    LayoutParameters = new GridLayout.LayoutParams(GridLayout.InvokeSpec(GridLayout.Undefined, 0f),
                        GridLayout.InvokeSpec(GridLayout.Undefined, 0f))
                };
                button.LayoutParameters.Width = ViewGroup.LayoutParams.WrapContent;
                button.LayoutParameters.Height = ViewGroup.LayoutParams.WrapContent;
                button.Gravity = GravityFlags.Center;

                parentLayout.AddView(textView);
                parentLayout.AddView(button);
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}