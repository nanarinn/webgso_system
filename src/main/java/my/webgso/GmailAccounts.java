package my.webgso;

/** Public account entry points accept the exact Gmail domain, never suffix lookalikes. */
public final class GmailAccounts {
  public static final String MESSAGE = "Only @gmail.com email addresses are allowed.";

  private GmailAccounts() {}

  public static boolean allowed(String email) {
    return email != null && email.length() <= 256 && email.matches("(?i)[^\\s@]+@gmail\\.com");
  }

  public static void require(String email) {
    if (!allowed(email)) throw new IllegalArgumentException(MESSAGE);
  }
}
