package my.webgso;

import java.util.Locale;

/** Exact company email domain policy for local account registration and sign-in. */
public final class SaeAccounts {
  private SaeAccounts() {}

  public static final String MESSAGE = "Use your @sae-malaysia.com email address.";

  public static boolean allowed(String email) {
    if (email == null || email.length() > 254 || !email.equals(email.strip())) return false;
    String value = email.toLowerCase(Locale.ROOT);
    return value.matches("[a-z0-9!#$%&'*+/=?^_`{|}~.-]{1,64}@sae-malaysia\\.com")
        && !value.startsWith(".") && !value.contains("..") && !value.contains(".@");
  }
}
