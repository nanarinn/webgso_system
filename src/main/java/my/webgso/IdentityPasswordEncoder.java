package my.webgso;

import java.nio.ByteBuffer;
import java.security.MessageDigest;
import java.util.*;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.PBEKeySpec;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

/** Java PBKDF2 implementation that preserves compatibility with the existing Identity database. */
public final class IdentityPasswordEncoder implements PasswordEncoder {
  private final BCryptPasswordEncoder bcrypt = new BCryptPasswordEncoder(12);

  public String encode(CharSequence raw) {
    byte[] salt = new byte[16];
    new java.security.SecureRandom().nextBytes(salt);
    var spec = new PBEKeySpec(raw.toString().toCharArray(), salt, 210000, 256);
    try {
      byte[] subkey =
          SecretKeyFactory.getInstance("PBKDF2WithHmacSHA512").generateSecret(spec).getEncoded();
      return Base64.getEncoder()
          .encodeToString(
              ByteBuffer.allocate(13 + salt.length + subkey.length)
                  .put((byte) 1)
                  .putInt(2)
                  .putInt(210000)
                  .putInt(salt.length)
                  .put(salt)
                  .put(subkey)
                  .array());
    } catch (java.security.GeneralSecurityException e) {
      throw new IllegalStateException("PBKDF2 is unavailable", e);
    } finally {
      spec.clearPassword();
    }
  }

  public boolean upgradeEncoding(String hash) {
    if (hash == null) return false;
    if (hash.startsWith("$2")) return true;
    try {
      byte[] data = Base64.getDecoder().decode(hash);
      return data[0] == 0 || data[0] == 1 && ByteBuffer.wrap(data, 5, 4).getInt() < 210000;
    } catch (Exception e) {
      return false;
    }
  }

  public boolean matches(CharSequence raw, String hash) {
    if (raw == null || hash == null) return false;
    if (hash.startsWith("$2")) return bcrypt.matches(raw, hash);
    try {
      byte[] data = Base64.getDecoder().decode(hash);
      if (data.length < 49) return false;
      int iterations, saltOffset, saltLength;
      String algorithm;
      if (data[0] == 0 && data.length == 49) {
        iterations = 1000;
        saltOffset = 1;
        saltLength = 16;
        algorithm = "PBKDF2WithHmacSHA1";
      } else if (data[0] == 1 && data.length >= 61) {
        ByteBuffer header = ByteBuffer.wrap(data, 1, 12);
        algorithm =
            switch (header.getInt()) {
              case 0 -> "PBKDF2WithHmacSHA1";
              case 1 -> "PBKDF2WithHmacSHA256";
              case 2 -> "PBKDF2WithHmacSHA512";
              default -> throw new IllegalArgumentException();
            };
        iterations = header.getInt();
        saltLength = header.getInt();
        saltOffset = 13;
      } else return false;
      if (iterations < 1
          || iterations > 10_000_000
          || saltLength < 16
          || saltOffset + saltLength + 16 > data.length) return false;
      byte[] salt = Arrays.copyOfRange(data, saltOffset, saltOffset + saltLength);
      byte[] expected = Arrays.copyOfRange(data, saltOffset + saltLength, data.length);
      var spec =
          new PBEKeySpec(raw.toString().toCharArray(), salt, iterations, expected.length * 8);
      try {
        return MessageDigest.isEqual(
            expected, SecretKeyFactory.getInstance(algorithm).generateSecret(spec).getEncoded());
      } finally {
        spec.clearPassword();
      }
    } catch (Exception e) {
      return false;
    }
  }
}
