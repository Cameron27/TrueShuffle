package com.csalisbury.trueshuffle;

public class State {
    public Type type;
    public String message;

    public State(Type type) {
        this.type = type;
        this.message = "";
    }

    public State(Type type, String message) {
        this.type = type;
        this.message = message;
    }

    public boolean isReadyState() {
        return type == Type.OK || type == Type.ERROR;
    }

    public enum Type {
        OK,
        LOGIN_FAILED,
        PROCESSING,
        ERROR
    }
}
