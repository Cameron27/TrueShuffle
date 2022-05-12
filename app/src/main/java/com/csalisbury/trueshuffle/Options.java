package com.csalisbury.trueshuffle;

import java.util.HashMap;
import java.util.Map;

public class Options extends HashMap<String, Object> {
    public Options(String s) {
        super();

        if(s.equals("")) return;

        String[] entries = s.split(" ");
        for (String entry : entries) {
            String[] e = entry.split("=");
            put(e[0], e[1]);
        }
    }

    public Options add(String s, Object o) {
        this.put(s, o);
        return this;
    }
}
