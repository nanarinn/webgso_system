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
}
