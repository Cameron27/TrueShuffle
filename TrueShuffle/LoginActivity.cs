using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Spotify.Sdk.Android.Authentication;
using Xamarin.Essentials;
using AlertDialog = Android.App.AlertDialog;

namespace TrueShuffle
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class LoginActivity : AppCompatActivity
    {
        private const string ClientId = "443f91927a354f6ea0f66c98629e8ebf";
        private const string RedirectUri = "com.cameronsalisbury.trueshuffle://callback";
        private const int RequestCode = 5684;
        private const string Scopes = "playlist-read-private playlist-modify-public playlist-modify-private";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_login);

            Button loginButton = FindViewById<Button>(Resource.Id.login_button);
            loginButton.Click += LoginOnClick;
        }

        private void LoginOnClick(object sender, EventArgs eventArgs)
        {
            AuthenticationRequest.Builder builder =
                new AuthenticationRequest.Builder(ClientId, AuthenticationResponse.Type.Token, RedirectUri);
            builder.SetScopes(new[] {Scopes});
            AuthenticationRequest request = builder.Build();
            AuthenticationClient.OpenLoginActivity(this, RequestCode, request);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent intent)
        {
            int resultCodeInt = (int) resultCode;

            if (requestCode != RequestCode) return;

            AuthenticationResponse response = AuthenticationClient.GetResponse(resultCodeInt, intent);
            if (response.GetType() == AuthenticationResponse.Type.Token)
            {
                TextView successTextView = FindViewById<TextView>(Resource.Id.success_text_view);
                successTextView.Text = "";

                Log.Debug("SpotifyAuth", $"Auth token: {response.AccessToken}");

                ISharedPreferencesEditor editor = GetSharedPreferences("SPOTIFY", 0).Edit();
                editor.PutString("token", response.AccessToken);
                editor.Commit();

                Intent i = new Intent(this, typeof(MainActivity));
                StartActivity(i);
            }
            else
            {
                TextView successTextView = FindViewById<TextView>(Resource.Id.success_text_view);
                successTextView.Text = "Failed to login";

                Log.Debug("SpotifyAuth", $"Auth error: {response.Error}");
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