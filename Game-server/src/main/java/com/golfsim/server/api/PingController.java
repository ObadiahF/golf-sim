package com.golfsim.server.api;

import com.golfsim.server.config.AppProperties;
import java.time.Clock;
import java.time.Instant;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api")
public class PingController {

    private final AppProperties appProperties;
    private final Clock clock;

    public PingController(AppProperties appProperties, Clock clock) {
        this.appProperties = appProperties;
        this.clock = clock;
    }

    @GetMapping("/ping")
    public PingResponse ping() {
        return new PingResponse(appProperties.name(), appProperties.version(), "ok", Instant.now(clock));
    }
}
