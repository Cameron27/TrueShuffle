package com.csalisbury.trueshuffle;

import android.view.View;
import android.widget.PopupWindow;

public class PopupWindowMod extends PopupWindow {
    private boolean cancelled = true;

    public PopupWindowMod(View popupView, int width, int height, boolean b) {
        super(popupView, width, height, b);
    }

    public void dismiss(boolean cancelled) {
        this.cancelled = cancelled;
        dismiss();
    }

    public boolean isCancelled() {
        return cancelled;
    }
}
