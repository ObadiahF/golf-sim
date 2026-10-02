package com.golfsim.server.stats;

import java.math.BigDecimal;
import java.math.RoundingMode;

/** One-decimal rounding for stats, halves away from zero: 2.75 becomes 2.8 and -2.75 becomes -2.8. */
final class OneDecimal {

    private OneDecimal() {
    }

    static double of(double value) {
        return BigDecimal.valueOf(value).setScale(1, RoundingMode.HALF_UP).doubleValue();
    }

    /** {@code sum / count}, computed exactly, then rounded. */
    static double ratio(long sum, long count) {
        return BigDecimal.valueOf(sum).divide(BigDecimal.valueOf(count), 1, RoundingMode.HALF_UP).doubleValue();
    }
}
