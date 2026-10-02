package com.golfsim.server;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.Map;

/**
 * Sends one HTTP/1.1 request over a plain socket and returns the status code. The request target goes out exactly
 * as given (no client-side normalising or re-encoding), which is what path-trick and malformed-query tests need.
 */
public final class RawHttp {

    private RawHttp() {
    }

    public static int status(int port, String method, String target, Map<String, String> headers, String body)
            throws IOException {
        try (Socket socket = new Socket("localhost", port)) {
            socket.setSoTimeout(5_000);
            byte[] content = body == null ? new byte[0] : body.getBytes(StandardCharsets.UTF_8);
            StringBuilder request = new StringBuilder()
                    .append(method).append(' ').append(target).append(" HTTP/1.1\r\n")
                    .append("Host: localhost\r\nConnection: close\r\n")
                    .append("Content-Length: ").append(content.length).append("\r\n");
            headers.forEach((name, value) -> request.append(name).append(": ").append(value).append("\r\n"));
            OutputStream out = socket.getOutputStream();
            out.write(request.append("\r\n").toString().getBytes(StandardCharsets.UTF_8));
            out.write(content);
            out.flush();
            String statusLine = new BufferedReader(
                    new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8)).readLine();
            return Integer.parseInt(statusLine.split(" ")[1]);
        }
    }

    public static int get(int port, String target) throws IOException {
        return status(port, "GET", target, Map.of(), null);
    }
}
