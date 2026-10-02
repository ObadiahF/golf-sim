package com.golfsim.server.api;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * A request body that bean validation can't check (e.g. a JSON object whose keys are data); answered like a
 * validation failure: {@code 400 "Validation failed"} with these {@code fieldErrors}.
 */
public class InvalidFieldsException extends RuntimeException {

    private final Map<String, String> fieldErrors;

    public InvalidFieldsException(Map<String, String> fieldErrors) {
        super("Validation failed: " + fieldErrors, null, false, false);
        this.fieldErrors = Collections.unmodifiableMap(new LinkedHashMap<>(fieldErrors));
    }

    public Map<String, String> fieldErrors() {
        return fieldErrors;
    }
}
