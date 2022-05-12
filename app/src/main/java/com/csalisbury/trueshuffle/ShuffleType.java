package com.csalisbury.trueshuffle;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import java.util.function.BiFunction;

import kaaes.spotify.webapi.android.models.PlaylistTrack;

public class ShuffleType {
    private String name;
    private String suffix;
    private int buttonId;
    private int inputId;
    private boolean takesInput;
    private BiFunction<List<PlaylistTrack>, String, List<PlaylistTrack>> shuffleFunction;

    public ShuffleType(String name, String suffix, int buttonId, BiFunction<List<PlaylistTrack>, String, List<PlaylistTrack>> shuffleFunction){
        this.name = name;
        this.suffix = suffix;
        this.buttonId = buttonId;
        this.inputId = -1;
        this.takesInput = false;
        this.shuffleFunction = shuffleFunction;
    }

    public ShuffleType(String name, String suffix, int buttonId, int inputId, BiFunction<List<PlaylistTrack>, String, List<PlaylistTrack>> shuffleFunction){
        this.name = name;
        this.suffix = suffix;
        this.buttonId = buttonId;
        this.inputId = inputId;
        this.takesInput = true;
        this.shuffleFunction = shuffleFunction;
    }

    public String getName() {
        return name;
    }

    public String getSuffix() {
        return suffix;
    }

    public int getButtonId() {
        return buttonId;
    }

    public int getInputId() {
        return inputId;
    }

    public boolean takesInput() {
        return takesInput;
    }

    public List<PlaylistTrack> shuffle(List<PlaylistTrack> tracks, String input) {
        return shuffleFunction.apply(tracks, input);
    }
}
