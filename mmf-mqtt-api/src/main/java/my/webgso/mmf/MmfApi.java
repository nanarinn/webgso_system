package my.webgso.mmf;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import javax.net.ssl.*;
import org.eclipse.paho.client.mqttv3.*;
import org.eclipse.paho.client.mqttv3.persist.MemoryPersistence;

public final class MmfApi {
  private static String env(String name, String fallback) {
    String value = System.getenv(name);
    return value == null || value.isBlank() ? fallback : value;
  }
  public static void main(String[] args) throws Exception {
    String broker = env("MMF_MQTT_URL", "ssl://56.69.184.157:8883");
    if (!broker.startsWith("ssl://")) throw new IllegalArgumentException("MQTT TLS is required.");
    String root = env("MMF_MQTT_ROOT", "LSS_BIDOR_30MW-NEDA");
    if (!root.matches("[A-Za-z0-9_-]{1,100}")) throw new IllegalArgumentException("Invalid topic root.");
    String password = env("MMF_MQTT_PASSWORD", "");
    if (password.isEmpty()) throw new IllegalArgumentException("Set MMF_MQTT_PASSWORD using the hidden prompt.");
    InetAddress bind = InetAddress.getByName(env("MMF_API_BIND", "127.0.0.1"));
    String apiToken = env("MMF_API_TOKEN", "");
    if (!bind.isLoopbackAddress() && apiToken.length() < 32)
      throw new IllegalArgumentException("Non-loopback binding requires a 32-character MMF_API_TOKEN and HTTPS reverse proxy.");
    LatestReadings readings = new LatestReadings(root);
    ObjectMapper json = new ObjectMapper();
    MqttClient client = new MqttClient(broker, "webgso-mmf-" + UUID.randomUUID(), new MemoryPersistence());
    client.setTimeToWait(15000);
    MqttConnectOptions options = new MqttConnectOptions();
    options.setUserName(env("MMF_MQTT_USERNAME", "bidor-viewer"));
    options.setPassword(password.toCharArray());
    options.setCleanSession(true); options.setAutomaticReconnect(true);
    options.setConnectionTimeout(10); options.setKeepAliveInterval(30);
    options.setHttpsHostnameVerificationEnabled(true);
    SSLSocketFactory factory = (SSLSocketFactory) SSLSocketFactory.getDefault();
    options.setSocketFactory(factory);
    java.util.concurrent.atomic.AtomicBoolean subscribed = new java.util.concurrent.atomic.AtomicBoolean();
    client.setCallback(new MqttCallbackExtended() {
      public void connectComplete(boolean reconnect, String serverURI) {
        try { client.subscribe(root + "/+/#", 1); subscribed.set(true); System.out.println("MQTT weather subscription ready."); }
        catch (MqttException error) { subscribed.set(false); System.err.println("MQTT subscription failed (code " + error.getReasonCode() + ")."); }
      }
      public void connectionLost(Throwable cause) { subscribed.set(false); System.err.println("MQTT disconnected; reconnecting."); }
      public void deliveryComplete(IMqttDeliveryToken token) {}
      public void messageArrived(String topic, MqttMessage message) {
        try { readings.accept(topic, message.getPayload(), Instant.now()); }
        catch (Exception ignored) { System.err.println("Ignored malformed MQTT weather message."); }
      }
    });
    HttpServer server = HttpServer.create(new InetSocketAddress(bind, Integer.parseInt(env("MMF_API_PORT", "5190"))), 20);
    ExecutorService httpThreads = Executors.newFixedThreadPool(4);
    server.setExecutor(httpThreads);
    server.createContext("/", exchange -> {
      try {
        exchange.getResponseHeaders().set("Cache-Control", "no-store");
        if (!"GET".equals(exchange.getRequestMethod())) { send(exchange, json, 405, Map.of("error", "Use GET.")); return; }
        if (!apiToken.isEmpty() && !MessageDigest.isEqual(("Bearer " + apiToken).getBytes(StandardCharsets.UTF_8),
            Objects.toString(exchange.getRequestHeaders().getFirst("Authorization"), "").getBytes(StandardCharsets.UTF_8))) {
          send(exchange, json, 401, Map.of("error", "API token required.")); return;
        }
        String path = exchange.getRequestURI().getPath();
        if (!path.equals("/health") && !path.equals("/api/mmf/BIDOR/latest")) {
          send(exchange, json, 404, Map.of("error", "Endpoint not found.")); return;
        }
        boolean connected = client.isConnected() && subscribed.get();
        if (path.equals("/health")) {
          send(exchange, json, connected ? 200 : 503, Map.of("mqttConnected", connected)); return;
        }
        String station = null;
        String query = exchange.getRequestURI().getRawQuery();
        if (query != null) for (String part : query.split("&")) {
          String[] pair = part.split("=", 2);
          if (pair[0].equals("station") && pair.length == 2) station = URLDecoder.decode(pair[1], StandardCharsets.UTF_8);
        }
        if (station != null && !station.matches("Weather_Station_SAE(?:_[1-9][0-9]*)?")) {
          send(exchange, json, 400, Map.of("error", "Invalid weather station.")); return;
        }
        var rows = readings.snapshot(station, Instant.now());
        Map<String, Object> body = new LinkedHashMap<>();
        body.put("plant", "BIDOR"); body.put("data_type", "MMF");
        body.put("mqttConnected", connected); body.put("maximumAgeSeconds", 300);
        body.put("status", !connected ? "Disconnected" : rows.isEmpty() ? "No data" :
            rows.stream().anyMatch(r -> "Stale".equals(r.get("status"))) ? "Stale" : "Fresh");
        body.put("readings", rows);
        send(exchange, json, connected && !rows.isEmpty() ? 200 : 503, body);
      } catch (Exception ignored) {
        try { send(exchange, json, 500, Map.of("error", "Unable to read MMF data.")); } catch (Exception closed) { exchange.close(); }
      }
    });
    server.start();
    System.out.println("MMF API listening on " + server.getAddress() + "/api/mmf/BIDOR/latest");
    ScheduledExecutorService connector = Executors.newSingleThreadScheduledExecutor();
    connector.scheduleWithFixedDelay(() -> {
      if (!client.isConnected()) try { client.connect(options); }
      catch (MqttException error) { System.err.println("MQTT connection unavailable (code " + error.getReasonCode() + "). Check credentials, network and trusted TLS certificate."); }
    }, 0, 30, TimeUnit.SECONDS);
    Runtime.getRuntime().addShutdownHook(new Thread(() -> {
      connector.shutdownNow(); server.stop(1); httpThreads.shutdownNow();
      try { client.disconnectForcibly(); client.close(); } catch (MqttException ignored) {}
    }));
  }
  private static void send(HttpExchange exchange, ObjectMapper json, int status, Object body) throws java.io.IOException {
    byte[] bytes = json.writeValueAsBytes(body);
    exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
    exchange.sendResponseHeaders(status, bytes.length);
    try (var out = exchange.getResponseBody()) { out.write(bytes); }
  }
}
