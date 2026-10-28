package my.webgso;

import static org.assertj.core.api.Assertions.*;

import java.net.*;
import java.nio.*;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.*;
import org.junit.jupiter.api.Test;

class PasswordAndNetworkTests {
  @Test
  void verifiesAspNetIdentityV3AndBcryptAndRejectsMalformedHashes() throws Exception {
    String password = "MigratedAccount123!";
    byte[] salt = new byte[16];
    new java.security.SecureRandom().nextBytes(salt);
    byte[] hash =
        SecretKeyFactory.getInstance("PBKDF2WithHmacSHA512")
            .generateSecret(new PBEKeySpec(password.toCharArray(), salt, 100000, 256))
            .getEncoded();
    byte[] identity =
        ByteBuffer.allocate(13 + salt.length + hash.length)
            .put((byte) 1)
            .putInt(2)
            .putInt(100000)
            .putInt(16)
            .put(salt)
            .put(hash)
            .array();
    var encoder = new IdentityPasswordEncoder();
    String encoded = Base64.getEncoder().encodeToString(identity);
    assertThat(encoder.matches(password, encoded)).isTrue();
    assertThat(encoder.matches("Wrong", encoded)).isFalse();
    assertThat(encoder.matches(password, encoder.encode(password))).isTrue();
    assertThat(
            encoder.matches(
                password,
                new org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder(4)
                    .encode(password)))
        .isTrue();
    var compatible = ByteBuffer.wrap(Base64.getDecoder().decode(encoder.encode(password)));
    assertThat(compatible.get()).isEqualTo((byte) 1);
    assertThat(compatible.getInt()).isEqualTo(2);
    assertThat(compatible.getInt()).isEqualTo(210000);
    assertThat(encoder.matches(password, "bad")).isFalse();
    assertThat(encoder.upgradeEncoding(encoded)).isTrue();
  }

  @Test
  void localHttpApiHostsAreAllowedAndMissingUrlsHaveClearMessage() {
    var client =
        new PlantApiClient(
            "localhost,127.0.0.1,::1", "localhost,127.0.0.1,::1,43.216.231.199", 1024);
    assertThat(client.validate("http://localhost:8000/BDNS/live/mmf").getScheme())
        .isEqualTo("http");
    assertThat(client.validate("http://43.216.231.199/kmpr/data").getHost())
        .isEqualTo("43.216.231.199");
    assertThatThrownBy(() -> client.validate("http://169.254.169.254/metadata"))
        .isInstanceOf(IllegalArgumentException.class);
    for (String missing : Arrays.asList(null, "", " "))
      assertThatThrownBy(() -> client.validate(missing))
          .isInstanceOf(IllegalArgumentException.class)
          .hasMessageContaining("No API URL is configured");
  }

  @Test
  void internalDestinationsRequireExplicitAllowlisting() throws Exception {
    for (String value :
        List.of(
            "127.0.0.1",
            "10.0.0.1",
            "172.16.0.1",
            "192.168.1.1",
            "169.254.169.254",
            "100.64.0.1",
            "::1",
            "fc00::1"))
      assertThat(PlantApiClient.isPrivate(InetAddress.getByName(value))).as(value).isTrue();
    assertThat(PlantApiClient.isPrivate(InetAddress.getByName("8.8.8.8"))).isFalse();
    var client = new PlantApiClient("", "", 1024);
    assertThatThrownBy(() -> client.validate("http://localhost:5186/private"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThatThrownBy(() -> client.validate("https://user:password@example.com/data"))
        .isInstanceOf(IllegalArgumentException.class);
    assertThat(client.validate("https://example.com/data").getHost()).isEqualTo("example.com");
    assertThat(
            new PlantApiClient("localhost", "localhost", 1024)
                .validate("http://localhost:5186/data"))
        .isNotNull();
  }

  @Test
  void diagnosticsRetainHttpErrorsWithoutFollowingRedirectsOrRemovingPayloadLimits()
      throws Exception {
    try (var server = new java.net.ServerSocket(0, 10, InetAddress.getByName("127.0.0.1"));
        var executor = java.util.concurrent.Executors.newSingleThreadExecutor()) {
      var hits = new java.util.concurrent.atomic.AtomicInteger();
      var serving =
          executor.submit(
              () -> {
                try {
                  for (int i = 0; i < 4; i++) {
                    try (var socket = server.accept()) {
                      socket.setSoTimeout(3000);
                      var reader =
                          new java.io.BufferedReader(
                              new java.io.InputStreamReader(
                                  socket.getInputStream(),
                                  java.nio.charset.StandardCharsets.US_ASCII));
                      String path = reader.readLine().split(" ")[1];
                      String line;
                      while ((line = reader.readLine()) != null && !line.isEmpty()) {}
                      int status =
                          path.equals("/denied") ? 401 : path.equals("/redirect") ? 302 : 200;
                      String body =
                          switch (path) {
                            case "/large" -> "x".repeat(256);
                            case "/denied" -> "{\"error\":\"unauthorized\"}";
                            default -> "{\"value\":1}";
                          };
                      if (path.equals("/ok")) hits.incrementAndGet();
                      byte[] bytes = body.getBytes(java.nio.charset.StandardCharsets.UTF_8);
                      String headers =
                          "HTTP/1.1 "
                              + status
                              + " Result\r\n"
                              + "Content-Type: application/json\r\n"
                              + "Connection: close\r\n"
                              + "Content-Length: "
                              + bytes.length
                              + "\r\n"
                              + (status == 302 ? "Location: /ok\r\n" : "")
                              + "\r\n";
                      socket
                          .getOutputStream()
                          .write(headers.getBytes(java.nio.charset.StandardCharsets.US_ASCII));
                      socket.getOutputStream().write(bytes);
                      socket.getOutputStream().flush();
                    }
                  }
                  return true;
                } catch (java.io.IOException e) {
                  throw new java.io.UncheckedIOException(e);
                }
              });
      var client = new PlantApiClient("127.0.0.1", "127.0.0.1", 128);
      String base = "http://127.0.0.1:" + server.getLocalPort();
      var response = client.fetch(base + "/ok");
      assertThat(response.status()).isEqualTo(200);
      assertThat(response.elapsedMillis()).isGreaterThanOrEqualTo(0);
      assertThatThrownBy(() -> client.fetch(base + "/denied"))
          .isInstanceOfSatisfying(
              PlantApiClient.Failure.class,
              failure -> {
                assertThat(failure.response().status()).isEqualTo(401);
                assertThat(failure.response().payload()).contains("unauthorized");
              });
      assertThatThrownBy(() -> client.fetch(base + "/redirect"))
          .hasMessageContaining("Redirects are not followed");
      assertThat(hits.get()).isEqualTo(1);
      assertThatThrownBy(() -> client.fetch(base + "/large")).hasMessageContaining("size limit");
      assertThat(serving.get(5, java.util.concurrent.TimeUnit.SECONDS)).isTrue();
    }
  }
}
