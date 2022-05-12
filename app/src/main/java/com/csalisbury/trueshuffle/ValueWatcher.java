package com.csalisbury.trueshuffle;

import java.util.function.Consumer;

public class ValueWatcher<T> {
    private T value;
    private final Consumer<T> watch;

    public ValueWatcher(T initialValue, Consumer<T> watch) {
        value = initialValue;
        this.watch = watch;
    }

    public T getValue() {
        return value;
    }

    public void setValue(T value) {
        this.value = value;
        watch.accept(value);
    }
}
