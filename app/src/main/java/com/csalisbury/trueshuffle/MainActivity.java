package com.csalisbury.trueshuffle;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.content.ContextCompat;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.drawable.ColorDrawable;
import android.os.Bundle;
import android.util.Log;
import android.view.Gravity;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.GridLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.spotify.sdk.android.auth.AuthorizationClient;
import com.spotify.sdk.android.auth.AuthorizationRequest;
import com.spotify.sdk.android.auth.AuthorizationResponse;

import java.security.InvalidParameterException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedList;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.concurrent.Semaphore;
import java.util.stream.Collectors;

import kaaes.spotify.webapi.android.SpotifyApi;
import kaaes.spotify.webapi.android.SpotifyService;
import kaaes.spotify.webapi.android.models.Pager;
import kaaes.spotify.webapi.android.models.PlaylistBase;
import kaaes.spotify.webapi.android.models.PlaylistSimple;
import kaaes.spotify.webapi.android.models.PlaylistTrack;
import kaaes.spotify.webapi.android.models.TrackToRemove;
import kaaes.spotify.webapi.android.models.TracksToRemove;
import kaaes.spotify.webapi.android.models.UserPrivate;
import retrofit.RetrofitError;

public class MainActivity extends AppCompatActivity {
    private static final String CLIENT_ID = "c4a1a641314d4d99b9df5304449a6c8d";
    private static final String REDIRECT_URI = "com.csalisbury.trueshuffle://callback";
    private static final int REQUEST_CODE = 31415;
    private static final String[] SCOPES = new String[]{"playlist-read-private playlist-modify-public playlist-modify-private"};
    private static final int PLAYLISTS_PER_PAGE = 10;
    private List<PlaylistBase> playlists;
    private List<PlaylistBase> playlistChildren;
    private SpotifyApi api;
    private SpotifyService spotify;
    private UserPrivate me;
    private int playlistIndex;
    private SwipeRefreshLayout swipe;
    private final Semaphore semaphore = new Semaphore(1);
    private ValueWatcher<State> currentState;
    private final ShuffleType[] SHUFFLE_TYPES = new ShuffleType[]{
            new ShuffleType("Shuffle", "Shuffled", R.id.shuffle_button, this::shuffleAll),
            new ShuffleType("Limit", "Limited", R.id.restrict_button, R.id.restrict_value, this::shuffleLimited)
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
    }

    @Override
    protected void onStart() {
        super.onStart();

        currentState = new ValueWatcher<>(new State(State.Type.OK), s -> runOnUiThread(() -> onStateChange(s)));

        login();

        findViewById(R.id.next_button).setOnClickListener(v -> populateUI(++playlistIndex * PLAYLISTS_PER_PAGE));
        findViewById(R.id.previous_button).setOnClickListener(v -> populateUI(--playlistIndex * PLAYLISTS_PER_PAGE));
        swipe = findViewById(R.id.refresh_view);
        swipe.setOnRefreshListener(this::onRefreshPage);
        findViewById(R.id.status_text_view).setVisibility(View.GONE);
    }

    private void onRefreshPage() {
        try {
            semaphore.acquire();
        } catch (Exception e) {
            return;
        }

        if (currentState.getValue().isReadyState()) {
            currentState.setValue(new State(State.Type.PROCESSING, "Refreshing playlists"));
            semaphore.release();
            loadPlaylists();
        } else if (currentState.getValue().type == State.Type.LOGIN_FAILED) {
            currentState.setValue(new State(State.Type.PROCESSING, "Logging in"));
            semaphore.release();
            login();
        } else {
            semaphore.release();
            currentState.setValue(new State(currentState.getValue().type, "Cannot refresh at the while something else is happening"));
            swipe.setRefreshing(false);
        }
    }

    private void login() {
        AuthorizationRequest.Builder builder = new AuthorizationRequest.Builder(CLIENT_ID, AuthorizationResponse.Type.TOKEN, REDIRECT_URI);

        builder.setScopes(SCOPES);
        AuthorizationRequest request = builder.build();

        AuthorizationClient.openLoginActivity(this, REQUEST_CODE, request);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, @Nullable Intent data) {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode == REQUEST_CODE) {
            AuthorizationResponse response = AuthorizationClient.getResponse(resultCode, data);

            switch (response.getType()) {
                case TOKEN:
                    Log.d("SpotifyAuth", "Auth token: " + response.getAccessToken());
                    currentState.setValue(new State(State.Type.OK));
                    setupApi(response.getAccessToken());
                    loadPlaylists();
                    break;
                case ERROR:
                    Log.d("SpotifyAuth", "Auth error: " + response.getError());
                    currentState.setValue(new State(State.Type.LOGIN_FAILED, "Failed to login"));
                    break;
                default:
                    Log.d("SpotifyAuth", "Auth aborted: " + response.getError());
                    currentState.setValue(new State(State.Type.LOGIN_FAILED, "Failed to login"));
            }
        }
    }

    private void setupApi(String accessToken) {
        api = new SpotifyApi();

        api.setAccessToken(accessToken);

        spotify = api.getService();
    }

    private void loadPlaylists() {
        new Thread(() -> {
            LinkedList<PlaylistBase> playlists = new LinkedList<>();
            Pager<PlaylistSimple> playlistResult;
            int i = 0;
            try {
                do {
                    playlistResult = spotify.getMyPlaylists(new Options(String.format("offset=%s limit=%s", i, 20)));
                    playlists.addAll(playlistResult.items);
                    i += 20;
                } while (playlistResult.next != null);
                currentState.setValue(new State(State.Type.OK));
            } catch (RetrofitError e) {
                Log.e("SpotifyAPI", "Error occurred fetching playlists: " + e);
                handleAPIError(e, "Failed to load playlists");
            }

            this.playlists = playlists.stream().filter(p -> {
                for (ShuffleType s : SHUFFLE_TYPES) {
                    if (p.name.endsWith(" " + s.getSuffix()))
                        return false;
                }
                return true;
            }).collect(Collectors.toList());

            this.playlistChildren = playlists.stream().filter(p -> {
                for (ShuffleType s : SHUFFLE_TYPES) {
                    if (p.name.endsWith(" " + s.getSuffix()))
                        return true;
                }
                return false;
            }).collect(Collectors.toList());

            playlistIndex = 0;
            runOnUiThread(() -> populateUI(0));
        }).start();
    }

    private void populateUI(int offset) {
        GridLayout parentLayout = findViewById(R.id.playlist_display);
        for (int i = offset; i < offset + PLAYLISTS_PER_PAGE; i++)
            if (i < playlists.size()) {
                TextView text = (TextView) parentLayout.getChildAt(i % PLAYLISTS_PER_PAGE * 2);
                Button button = (Button) parentLayout.getChildAt(i % PLAYLISTS_PER_PAGE * 2 + 1);

                text.setText(playlists.get(i).name);
                PlaylistBase playlist = playlists.get(i);

                button.setOnClickListener(v -> onDisplayShuffleDialog(playlist));

                text.setVisibility(View.VISIBLE);
                button.setVisibility(View.VISIBLE);

            } else {
                parentLayout.getChildAt(i % PLAYLISTS_PER_PAGE * 2).setVisibility(View.GONE);
                parentLayout.getChildAt(i % PLAYLISTS_PER_PAGE * 2 + 1).setVisibility(View.GONE);
            }

        Button nextButton = findViewById(R.id.next_button);
        nextButton.setVisibility(offset + PLAYLISTS_PER_PAGE < playlists.size()
                ? View.VISIBLE
                : View.INVISIBLE);

        Button previousButton = findViewById(R.id.previous_button);
        previousButton.setVisibility(offset > 0 ? View.VISIBLE : View.INVISIBLE);
        swipe.setRefreshing(false);
    }

    private void onDisplayShuffleDialog(PlaylistBase playlist) {
        try {
            semaphore.acquire();
        } catch (Exception e) {
            return;
        }
        if (!currentState.getValue().isReadyState())
            return;

        currentState.setValue(new State(State.Type.PROCESSING));
        semaphore.release();

        LayoutInflater inflater = (LayoutInflater) getSystemService(LAYOUT_INFLATER_SERVICE);
        View popupView = inflater.inflate(R.layout.shuffle_popup, null);

        new Thread(() -> runOnUiThread(() -> {
            for (ShuffleType s : SHUFFLE_TYPES) {
                if (!s.takesInput()) continue;
                String value = getSharedPreferences("SPOTIFY", MODE_PRIVATE).getString(s.getName() + "_input", "");
                if (value.equals("")) continue;
                ((EditText) popupView.findViewById(s.getInputId())).setText(value);
            }
        })).start();

        int width = LinearLayout.LayoutParams.MATCH_PARENT;
        int height = LinearLayout.LayoutParams.WRAP_CONTENT;
        PopupWindowMod pw = new PopupWindowMod(popupView, width, height, true);
        pw.setBackgroundDrawable(new ColorDrawable(ContextCompat.getColor(this, R.color.spotify_grey)));
        pw.showAtLocation(swipe, Gravity.CENTER, 0, 0);
        pw.setOnDismissListener(() -> {
            if (pw.isCancelled()) {
                currentState.setValue(new State(State.Type.OK));
            }
        });


        for (ShuffleType s : SHUFFLE_TYPES) {
            Button button = popupView.findViewById(s.getButtonId());
            button.setText(s.getName());
            button.setOnClickListener(v -> {
                pw.dismiss(false);

                String input = null;
                if (s.takesInput()) {
                    EditText et = popupView.findViewById(s.getInputId());
                    input = et.getText().toString();
                    SharedPreferences.Editor edit = getSharedPreferences("SPOTIFY", MODE_PRIVATE).edit();
                    edit.putString(s.getName() + "_input", input);
                    edit.apply();
                }
                shuffle(playlist, s, input);
            });
        }

        ((EditText) popupView.findViewById(R.id.restrict_value)).setText(
                getSharedPreferences("SPOTIFY", 0).getString("RESTRICT_VALUE", "10"));
    }

    private void shuffle(PlaylistBase playlist, ShuffleType shuffleType, String input) {
        new Thread(() -> {
            currentState.setValue(new State(State.Type.PROCESSING, "Loading songs"));

            List<PlaylistTrack> tracks;
            try {
                tracks = getAllTracks(playlist, true);
            } catch (RetrofitError e) {
                Log.e("SpotifyAPI", "Error occurred fetching tracks: " + e);
                handleAPIError(e, "Failed to load songs");
                return;
            }

            tracks = shuffleType.shuffle(tracks, input);

            PlaylistBase playlistChild = null;
            for (PlaylistBase p : playlistChildren) {
                if (p.name.equals(playlist.name + " " + shuffleType.getSuffix())) {
                    playlistChild = p;

                    currentState.setValue(new State(State.Type.PROCESSING, "Deleting old songs"));
                    try {
                        List<PlaylistTrack> existingTracks = getAllTracks(playlistChild, false);
                        deleteAllTracks(playlistChild, existingTracks);
                    } catch (RetrofitError e) {
                        Log.e("SpotifyAPI", "Error occurred deleting tracks: " + e);
                        handleAPIError(e, "Failed to delete old songs");
                        return;
                    }

                    break;
                }
            }

            if (playlistChild == null) {
                currentState.setValue(new State(State.Type.PROCESSING, "Creating new playlist"));
                try {
                    playlistChild = spotify.createPlaylist(playlist.owner.id, new Options("").add("name", String.format("%s %s", playlist.name, shuffleType.getSuffix())).add("public", false));
                } catch (RetrofitError e) {
                    Log.e("SpotifyAPI", "Error occurred creating new playlist: " + e);
                    handleAPIError(e, "Failed to create new playlist");
                    return;
                }
                playlistChildren.add(playlistChild);
            }

            currentState.setValue(new State(State.Type.PROCESSING, "Adding songs"));
            try {
                addTracks(playlistChild, tracks);
            } catch (RetrofitError e) {
                Log.e("SpotifyAPI", "Error occurred adding tracks: " + e);
                handleAPIError(e, "Failed to add songs");
                return;
            }

            currentState.setValue(new State(State.Type.OK));
        }).start();
    }

    private List<PlaylistTrack> getAllTracks(PlaylistBase playlist, boolean requireArtists) {
        int trackCount = spotify.getPlaylist(playlist.owner.id, playlist.id, new Options("fields=tracks.total")).tracks.total;

        String fields = requireArtists
                ? "items.track(uri,artists.name)"
                : "items.track(uri)";

        List<PlaylistTrack> tracks = new ArrayList<>();
        for (int i = 0; i < (int) Math.ceil(trackCount) / 100f; i++) {
            Pager<PlaylistTrack> playlistTracks = spotify.getPlaylistTracks(playlist.owner.id, playlist.id, new Options(String.format("limit=100 offset=%s fields=%s", i * 100, fields)));
            tracks.addAll(playlistTracks.items);
        }
        return tracks;
    }

    private void deleteAllTracks(PlaylistBase playlist, List<PlaylistTrack> tracks) {
        for (int i = 0; i < (int) Math.ceil(tracks.size()) / 100f; i++) {
            TracksToRemove tracksToRemove = new TracksToRemove();
            tracksToRemove.tracks = tracks.stream()
                    .skip(i * 100L)
                    .limit(100)
                    .map(t -> {
                        TrackToRemove ttr = new TrackToRemove();
                        ttr.uri = t.track.uri;
                        return ttr;
                    })
                    .collect(Collectors.toList());

            spotify.removeTracksFromPlaylist(playlist.owner.id, playlist.id, tracksToRemove);
        }
    }

    private void addTracks(PlaylistBase playlist, List<PlaylistTrack> tracks) {
        for (int i = 0; i < (int) Math.ceil(tracks.size()) / 100f; i++) {
            TracksToRemove tracksToRemove = new TracksToRemove();
            tracksToRemove.tracks = tracks.stream()
                    .skip(i * 100L)
                    .limit(100)
                    .map(t -> {
                        TrackToRemove ttr = new TrackToRemove();
                        ttr.uri = t.track.uri;
                        return ttr;
                    })
                    .collect(Collectors.toList());

            spotify.addTracksToPlaylist(playlist.owner.id, playlist.id,
                    new Options(""),
                    new Options("position=" + (i * 100))
                            .add("uris", tracks.stream().skip(i * 100L).limit(100).map(t -> t.track.uri).toArray())
            );
        }
    }

    private void onStateChange(State s) {
        TextView v = findViewById(R.id.status_text_view);
        if (!s.message.equals("")) {
            v.setText(s.message);
            v.setVisibility(View.VISIBLE);
        } else {
            v.setVisibility(View.GONE);
        }

        GridLayout parentLayout = findViewById(R.id.playlist_display);
        for (int i = 1; i < parentLayout.getChildCount(); i += 2) {
            Button button = (Button) parentLayout.getChildAt(i);
            boolean active = currentState.getValue().isReadyState();
            button.setActivated(active);
            int color = active
                    ? R.color.spotify_green
                    : R.color.spotify_grey;
            button.setBackgroundColor(ContextCompat.getColor(this, color));
        }
    }

    private void handleAPIError(RetrofitError e, String defaultMessage) {
        int resCode = e.getResponse().getStatus();
        switch (resCode) {
            case 401:
                currentState.setValue(new State(State.Type.LOGIN_FAILED, "Session expires, please refresh page to log back in"));
                break;
            case 403:
                currentState.setValue(new State(State.Type.ERROR, "Rate limit exceeded"));
                break;
            default:
                currentState.setValue(new State(State.Type.ERROR, defaultMessage));
        }
    }

    private List<PlaylistTrack> shuffleAll(List<PlaylistTrack> tracks, String input) {
        List<PlaylistTrack> result = new ArrayList<>(tracks);
        randomizeList(result);
        return result;
    }

    private List<PlaylistTrack> shuffleLimited(List<PlaylistTrack> tracks, String input) {
        int maxPerArtist;
        try {
            maxPerArtist = Integer.parseInt(input);
            if (maxPerArtist <= 0) throw new Exception();
        } catch (Exception e) {
            currentState.setValue(new State(State.Type.ERROR, "Input must be a positive"));
            throw new InvalidParameterException(String.format("Input value \"%s\" is not a positive integer.", input));
        }

        List<PlaylistTrack> result = new ArrayList<>();
        Map<String, List<PlaylistTrack>> tracksByArtist = tracks.stream().collect(Collectors.groupingBy(t -> t.track.artists.get(0).name));
        tracksByArtist.forEach((artist, ts) -> {
            randomizeList(ts);
            result.addAll(ts.stream().limit(maxPerArtist).collect(Collectors.toList()));
        });

        randomizeList(result);
        return result;
    }

    private static <T> void randomizeList(List<T> list) {
        Random rnd = new Random();
        for (int i = 0; i < list.size() - 2; i++) {
            int j = rnd.nextInt(list.size() - i) + i;
            T temp = list.get(i);
            list.set(i, list.get(j));
            list.set(j, temp);
        }
    }
}