package com.golfsim.server.game;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

/** GS-5, GS-6, GS-8, GS-12: name rules. */
class NamesTest {

    private static final String GRIN = "😀"; // one code point, two UTF-16 units

    @Test
    void lengthCountsCodePoints() {
        assertThat(Names.problem(GRIN.repeat(21))).isEmpty();
        assertThat(Names.problem(GRIN.repeat(40))).isEmpty();
        assertThat(Names.problem(GRIN.repeat(41))).isPresent();
        assertThat(Names.problem("a".repeat(40))).isEmpty();
        assertThat(Names.problem("a".repeat(41))).isPresent();
        assertThat(Names.problem("👨‍👩‍👧")).isEmpty(); // ZWJ family emoji
    }

    @ParameterizedTest
    @ValueSource(strings = {"", " ", "​", " ", "　", "​​", "‍", "́",
            "tab\tname", "new\nline", "a\u0000b", "a\u0007b", "a​b", "a‮b", "a b", "a﻿b",
            "\uD800x", "ab"})
    void controlInvisibleAndBlankNamesAreRejected(String name) {
        assertThat(Names.problem(name)).as(name).isPresent();
    }

    @Test
    void nullIsRejected() {
        assertThat(Names.problem(null)).isPresent();
    }

    @Test
    void namesAreNormalised() {
        assertThat(Names.normalize("  Obi   Wan ")).isEqualTo("Obi Wan");
        assertThat(Names.normalize("Ｏｂｉ")).isEqualTo("Obi"); // NFKC: fullwidth letters
        assertThat(Names.normalize("José")).isEqualTo("José"); // composed
        assertThat(Names.normalize("a b")).isEqualTo("a b");
    }

    @Test
    void keysFoldCaseIncludingTurkishAndGermanSpecialCases() {
        assertThat(Names.key("İvan")).isEqualTo(Names.key("ivan")).isEqualTo(Names.key("IVAN"))
                .isEqualTo(Names.key("ıvan")).isEqualTo("ivan");
        assertThat(Names.key("Straße")).isEqualTo(Names.key("STRASSE"));
        assertThat(Names.key(" José ")).isEqualTo(Names.key("JOSÉ"));
        assertThat(Names.key("Obi")).isNotEqualTo(Names.key("Obi2"));
    }

    @Test
    void deviceNamesAreCutOnCodePointBoundariesAndCleaned() {
        String a39 = "A".repeat(39);
        assertThat(Names.deviceName(a39 + GRIN)).isEqualTo(a39 + GRIN);
        assertThat(Names.deviceName("A".repeat(40) + GRIN)).isEqualTo("A".repeat(40));
        assertThat(Names.deviceName(GRIN.repeat(50))).isEqualTo(GRIN.repeat(40));
        assertThat(Names.deviceName("Obi's\u0000 iPhone​")).isEqualTo("Obi's iPhone");
        assertThat(Names.deviceName("​​")).isNull();
        assertThat(Names.deviceName("   ")).isNull();
        assertThat(Names.deviceName(null)).isNull();
        String cut = Names.deviceName("A".repeat(39) + GRIN + GRIN);
        assertThat(cut.codePoints().noneMatch(cp -> cp >= Character.MIN_SURROGATE && cp <= Character.MAX_SURROGATE)).isTrue();
    }
}
