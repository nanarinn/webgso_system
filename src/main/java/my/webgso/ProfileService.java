package my.webgso;

import java.util.*;
import my.webgso.Models.ApplicationUser;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class ProfileService {
  private final Store db;
  private final Accounts accounts;

  public ProfileService(Store db, Accounts accounts) {
    this.db = db;
    this.accounts = accounts;
  }

  @Transactional
  public void update(
      String userId, String displayName, String username, String email, String phone) {
    displayName = displayName.trim();
    username = username.trim();
    email = email.trim();
    phone = phone.trim();
    if (displayName.isBlank()
        || displayName.length() > 160
        || !username.matches("[a-zA-Z0-9_.@-]{3,100}")
        || email.length() > 256
        || !email.matches("[^\\s@]+@[^\\s@]+\\.[^\\s@]+")
        || !phone.matches("[+0-9() .-]{0,30}"))
      throw new IllegalArgumentException("Enter a valid name, username, email and phone number.");
    var user =
        db.list(ApplicationUser.class, "WHERE \"Id\"=? FOR UPDATE", userId).stream()
            .findFirst()
            .orElseThrow(() -> new IllegalArgumentException("Profile not found."));
    if (Boolean.TRUE.equals(user.isEntraUser)
        && (!username.equals(user.userName) || !email.equals(user.email)))
      throw new IllegalArgumentException(
          "Your username and email are managed by your identity provider.");
    String normalizedName = username.toUpperCase(Locale.ROOT),
        normalizedEmail = email.toUpperCase(Locale.ROOT);
    if (!db.list(
            ApplicationUser.class,
            "WHERE \"Id\"<>? AND (\"NormalizedUserName\" IN (?,?) OR \"NormalizedEmail\" IN (?,?))",
            userId,
            normalizedName,
            normalizedEmail,
            normalizedName,
            normalizedEmail)
        .isEmpty()) throw new IllegalArgumentException("That username or email is already in use.");
    db.jdbc()
        .update(
            "UPDATE \"AspNetUsers\" SET \"DisplayName\"=?, \"UserName\"=?,"
                + " \"NormalizedUserName\"=?, \"Email\"=?, \"NormalizedEmail\"=?,"
                + " \"PhoneNumber\"=?, \"EmailConfirmed\"=?, \"PhoneNumberConfirmed\"=?,"
                + " \"ConcurrencyStamp\"=? WHERE \"Id\"=?",
            displayName,
            username,
            normalizedName,
            email,
            normalizedEmail,
            phone.isBlank() ? null : phone,
            email.equalsIgnoreCase(user.email) && Boolean.TRUE.equals(user.emailConfirmed),
            Objects.equals(phone, Objects.toString(user.phoneNumber, ""))
                && Boolean.TRUE.equals(user.phoneNumberConfirmed),
            UUID.randomUUID().toString(),
            userId);
    accounts.audit(
        username, "Update own profile", "User", userId, "Success", "Profile details updated");
  }
}
