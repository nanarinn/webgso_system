package my.webgso;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Locale;
import org.springframework.security.authentication.AccountStatusException;

public class DeletedAccountException extends AccountStatusException {
  public DeletedAccountException() {
    super("Your account has been deleted. Please sign up again.");
  }

  static String identifierHash(String identifier) {
    try {
      return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256")
          .digest(identifier.trim().toUpperCase(Locale.ROOT).getBytes(StandardCharsets.UTF_8)));
    } catch (NoSuchAlgorithmException error) {
      throw new IllegalStateException("SHA-256 is unavailable", error);
    }
  }
}
