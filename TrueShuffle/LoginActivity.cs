using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Support.V7.App;
using Android.Runtime;
using Android.Util;
using Android.Widget;
using Com.Spotify.Sdk.Android.Authentication;

namespace TrueShuffle
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class LoginActivity : AppCompatActivity
    {
        private const string ClientId = "443f91927a354f6ea0f66c98629e8ebf";
        private const string RedirectUri = "com.cameronsalisbury.trueshuffle://callback";
        private const int RequestCode = 5684;
        private const string Scopes = "playlist-read-private playlist-modify-public playlist-modify-private user-read-private";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_login);

            Button loginButton = FindViewById<Button>(Resource.Id.login_button);
            loginButton.Click += LoginOnClick;

            // if token already saved, go to main
//            if (GetSharedPreferences("SPOTIFY", 0).GetString("token", "") != "")
//            { 
//                Intent i = new Intent(this, typeof(MainActivity));
//                StartActivity(i);
//            }
        }

        private void LoginOnClick(object sender, EventArgs eventArgs)
        {
            AuthenticationRequest.Builder builder =
                new AuthenticationRequest.Builder(ClientId, AuthenticationResponse.Type.Token, RedirectUri);
            builder.SetScopes(new[] { Scopes });
            AuthenticationRequest request = builder.Build();
            AuthenticationClient.OpenLoginActivity(this, RequestCode, request);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent intent)
        {
            int resultCodeInt = (int)resultCode;
            resultCodeInt += 0;

            if (requestCode != RequestCode) return;

            AuthenticationResponse response = AuthenticationClient.GetResponse(resultCodeInt, intent);
            if (response.GetType() == AuthenticationResponse.Type.Token)
            {
                Log.Debug("SpotifyAuth", $"Auth token: {response.AccessToken}");

                ISharedPreferencesEditor editor = GetSharedPreferences("SPOTIFY", 0).Edit();
                editor.PutString("token", response.AccessToken);
                editor.Commit();

                Intent i = new Intent(this, typeof(MainActivity));
                StartActivity(i);
            }
            else if (response.GetType() == AuthenticationResponse.Type.Error)
            {
                Log.Debug("SpotifyAuth", $"Auth error: {response.Error}");
            }
            else
            {
                Log.Debug("SpotifyAuth", "Auth failed");
            }
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}