package com.golfsim.server.api;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.delete;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.golfsim.server.IntegrationTest;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

class PhysicsControllerTest extends IntegrationTest {

    @Autowired
    private MockMvc mockMvc;

    private ResultActions call(MockHttpServletRequestBuilder request) throws Exception {
        return mockMvc.perform(request.header(HttpHeaders.AUTHORIZATION, "Bearer " + TOKEN));
    }

    private ResultActions putJson(String json) throws Exception {
        return call(put("/api/physics").contentType(MediaType.APPLICATION_JSON).content(json));
    }

    @Test
    void startsWithEverySurfaceAndNoOverrides() throws Exception {
        call(get("/api/physics"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.surfaces.length()").value(8))
                .andExpect(jsonPath("$.surfaces[0].surface").value("green"))
                .andExpect(jsonPath("$.surfaces[7].surface").value("bunker"))
                .andExpect(jsonPath("$.surfaces[0].rolling").doesNotExist())
                .andExpect(jsonPath("$.surfaces[0].restitution").doesNotExist())
                .andExpect(jsonPath("$.surfaces[0].friction").doesNotExist());
    }

    @Test
    void partialUpdatesMergeAndPersist() throws Exception {
        putJson("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":0.07},{\"surface\":\"rough\",\"friction\":0.6,\"restitution\":0.5}]}")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.surfaces[0].rolling").value(0.07))
                .andExpect(jsonPath("$.surfaces[0].friction").doesNotExist())
                .andExpect(jsonPath("$.surfaces[3].surface").value("rough"))
                .andExpect(jsonPath("$.surfaces[3].friction").value(0.6));
        // A second update keeps what it doesn't mention, replaces numbers and clears nulls.
        putJson("{\"surfaces\":[{\"surface\":\"rough\",\"friction\":null,\"rolling\":1}]}").andExpect(status().isOk());
        call(get("/api/physics"))
                .andExpect(jsonPath("$.surfaces[0].rolling").value(0.07))
                .andExpect(jsonPath("$.surfaces[3].friction").doesNotExist())
                .andExpect(jsonPath("$.surfaces[3].restitution").value(0.5))
                .andExpect(jsonPath("$.surfaces[3].rolling").value(1.0));
        assertThat(jdbc.queryForObject("select count(*) from physics_overrides", Long.class)).isEqualTo(3);
        putJson("{\"surfaces\":[]}").andExpect(status().isOk()).andExpect(jsonPath("$.surfaces[0].rolling").value(0.07));
    }

    @Test
    void deleteResetsToDefaults() throws Exception {
        putJson("{\"surfaces\":[{\"surface\":\"bunker\",\"rolling\":2,\"restitution\":0.1,\"friction\":1}]}").andExpect(status().isOk());
        call(delete("/api/physics"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.surfaces[7].surface").value("bunker"))
                .andExpect(jsonPath("$.surfaces[7].rolling").doesNotExist());
        assertThat(jdbc.queryForObject("select count(*) from physics_overrides", Long.class)).isZero();
    }

    @Test
    void rangesAreValidated() throws Exception {
        putJson("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":0.01,\"restitution\":1.3,\"friction\":-0.1},"
                + "{\"surface\":\"fairway\",\"rolling\":3.5}]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Validation failed"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[0].rolling']").value("must be between 0.02 and 3"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[0].restitution']").value("must be between 0 and 1.2"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[0].friction']").value("must be between 0 and 1.5"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[1].rolling']").value("must be between 0.02 and 3"));
        // Bounds are inclusive.
        putJson("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":0.02,\"restitution\":1.2,\"friction\":0}]}")
                .andExpect(status().isOk());
    }

    @Test
    void unknownSurfacesFieldsAndBadValuesAreRefusedAndChangeNothing() throws Exception {
        putJson("{\"surfaces\":[{\"surface\":\"lava\",\"rolling\":0.5},{\"surface\":\"green\",\"spin\":1,\"rolling\":\"0.07\"},"
                + "{\"surface\":\"green\"},{\"rolling\":0.1},7],\"reset\":true}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['surfaces[0].surface']").value(
                        "unknown surface 'lava' (one of green, fairway, tee, rough, native, scrub, woods, bunker)"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[1].spin']").value(
                        "unknown field (allowed: surface, rolling, restitution, friction)"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[1].rolling']").value("must be a finite number or null"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[2].surface']").value("duplicate surface 'green'"))
                .andExpect(jsonPath("$.fieldErrors['surfaces[3].surface']").exists())
                .andExpect(jsonPath("$.fieldErrors['surfaces[4]']").value("must be an object"))
                .andExpect(jsonPath("$.fieldErrors.reset").value("unknown field (allowed: surfaces)"));
        putJson("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":0.07},{\"surface\":\"water\",\"rolling\":1}]}")
                .andExpect(status().isBadRequest());
        assertThat(jdbc.queryForObject("select count(*) from physics_overrides", Long.class)).isZero();
    }

    @Test
    void malformedBodiesAreRefused() throws Exception {
        putJson("{}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.surfaces").value("must not be null (reset with DELETE /api/physics)"));
        putJson("{\"surfaces\":{}}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.surfaces").value("must be an array"));
        putJson("[]").andExpect(status().isBadRequest()).andExpect(jsonPath("$.message").value("Expected a JSON object"));
        putJson("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":1e400}]}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['surfaces[0].rolling']").value("must be a finite number or null"));
        putJson("{\"surfaces\":[]}garbage").andExpect(status().isBadRequest());
        putJson("{\"surfaces\":[],\"surfaces\":[]}").andExpect(status().isBadRequest());
        putJson("{not json").andExpect(status().isBadRequest());
        call(post("/api/physics").contentType(MediaType.APPLICATION_JSON).content("{}"))
                .andExpect(status().isMethodNotAllowed());
    }

    @Test
    void needsTheToken() throws Exception {
        mockMvc.perform(get("/api/physics")).andExpect(status().isUnauthorized());
        mockMvc.perform(delete("/api/physics")).andExpect(status().isUnauthorized());
        mockMvc.perform(put("/api/physics").contentType(MediaType.APPLICATION_JSON)
                .content("{\"surfaces\":[{\"surface\":\"green\",\"rolling\":0.5}]}")).andExpect(status().isUnauthorized());
        assertThat(jdbc.queryForObject("select count(*) from physics_overrides", Long.class)).isZero();
    }
}
