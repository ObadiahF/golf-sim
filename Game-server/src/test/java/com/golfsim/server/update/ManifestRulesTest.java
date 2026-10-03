package com.golfsim.server.update;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class ManifestRulesTest {

    @Test
    void ordinaryBuildPathsAreFine() {
        for (String path : List.of("GolfSim.exe", "GolfSim_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll",
                "Contents/MacOS/Golf-sim", "Contents/Resources/Data/StreamingAssets/a b (1).json", ".hidden", "D3D12/D3D12Core.dll")) {
            assertThat(ManifestRules.pathProblem(path)).as(path).isNull();
        }
    }

    @Test
    void escapesAreRefused() {
        for (String path : List.of("/abs", "..", "a/../b", "./a", "a/.", "C:/x", "C:x", "\\\\server\\share", "a\\b",
                "a:b", "aux", "LPT1.txt", "a/b.", "a/ b", "a\u0000b", "a\u0085b", "x".repeat(401))) {
            assertThat(ManifestRules.pathProblem(path)).as(path).isNotNull();
        }
    }

    @Test
    void collisionsAreFound() {
        assertThat(ManifestRules.conflicts(List.of("a/b", "A/B", "c"))).isEqualTo(Map.of(1, "duplicate of files[0] (paths are compared ignoring case)"));
        assertThat(ManifestRules.conflicts(List.of("a/b/c", "A/b"))).containsOnlyKeys(1);
        assertThat(ManifestRules.conflicts(List.of("ab", "a/b", "a.b"))).isEmpty();
    }
}
