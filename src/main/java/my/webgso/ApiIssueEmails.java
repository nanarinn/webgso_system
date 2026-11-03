package my.webgso;

import java.util.*;
import my.webgso.Models.*;
import org.springframework.core.env.Environment;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class ApiIssueEmails {
  public record Issue(ApiTestHistoryService.Entry test, String plant, String code, String reason) {}

  private final Store db;
  private final ApiTestHistoryService history;
  private final Accounts accounts;
  private final Environment env;

  public ApiIssueEmails(
      Store db, ApiTestHistoryService history, Accounts accounts, Environment env) {
    this.db = db;
    this.history = history;
    this.accounts = accounts;
    this.env = env;
  }

  public List<ApplicationUser> recipients() {
    return db
        .list(
            ApplicationUser.class,
            "WHERE \"AccessStatus\"='Approved' AND \"EmailConfirmed\"=true ORDER BY \"Email\"")
        .stream()
        .filter(u -> GmailAccounts.allowed(u.email))
        .toList();
  }

  public List<Issue> issues() {
    var latest = history.latest();
    var links = db.list(PlantApiConfiguration.class, "");
    var results = new ArrayList<Issue>();
    for (var plant : db.list(LssPlant.class, "")) {
      for (var dataset : ConfiguredDatasets.forPlant(plant, links)) {
        var test = latest.get(ApiTestHistoryService.key(plant.id, dataset));
        if (test == null) continue;
        String message = Objects.toString(test.message(), "").toLowerCase(Locale.ROOT);
        boolean empty =
            message.matches("(?s).*\\b0 (readable|reading|measurement|value).*")
                || message.contains("no readable")
                || message.contains("no values")
                || message.contains("no measurements")
                || message.contains("empty response");
        if ("Failed".equals(test.outcome()) || empty)
          results.add(
              new Issue(
                  test, plant.name, plant.plantId, empty ? "No API values" : "API test failed"));
      }
    }
    results.sort(Comparator.comparing((Issue i) -> i.test().testedAt()).reversed());
    return results;
  }

  @Transactional
  public int queue(List<Long> selected, List<String> recipients, String sender) {
    if (!env.getProperty("webgso.mail.enabled", Boolean.class, false))
      throw new IllegalArgumentException(
          "Email delivery is not configured. Configure Gmail SMTP before sending.");
    var ids = new TreeSet<>(selected);
    var users = new LinkedHashSet<>(recipients);
    if (ids.isEmpty() || users.isEmpty())
      throw new IllegalArgumentException(
          "Select at least one API issue and one registered recipient.");
    if (ids.size() > 100 || users.size() > 50)
      throw new IllegalArgumentException(
          "Select up to 100 API issues and 50 recipients per message.");
    var available = new HashMap<Long, Issue>();
    issues().forEach(i -> available.put(i.test().id(), i));
    if (!available.keySet().containsAll(ids))
      throw new IllegalArgumentException(
          "An API result changed or was removed. Refresh and select the latest issues.");
    var eligible = new HashMap<String, ApplicationUser>();
    recipients().forEach(u -> eligible.put(u.id, u));
    if (!eligible.keySet().containsAll(users))
      throw new IllegalArgumentException(
          "Choose active, email-verified accounts from the registered recipient list.");
    StringBuilder body = new StringBuilder("GSO Web Services - API issue report\n\n");
    for (long id : ids) {
      var issue = available.get(id);
      var test = issue.test();
      body.append("Plant: ")
          .append(issue.plant())
          .append(" (")
          .append(issue.code())
          .append(")\nAPI: ")
          .append(test.dataset())
          .append("\nIssue: ")
          .append(issue.reason())
          .append("\nHTTP: ")
          .append(test.httpStatus() == null ? "No response" : test.httpStatus())
          .append("\nTested at (MYT): ")
          .append(test.testedAt().atZoneSameInstant(Policies.LOCAL))
          .append("\nResult ID: ")
          .append(test.id())
          .append("\n\n");
    }
    body.append(
        "Open API test history in GSO Web Services to review these results. API URLs, credentials"
            + " and raw payloads are not included.");
    int count = 0;
    for (String id : users) {
      // Serialize duplicate checks for each recipient, including simultaneous clicks.
      db.jdbc()
          .queryForList(
              "SELECT \"Id\" FROM \"AspNetUsers\" WHERE \"Id\"=? FOR UPDATE", String.class, id);
      if (!db.find(ApplicationUser.class, id)
          .filter(
              u ->
                  "Approved".equals(u.accessStatus)
                      && Boolean.TRUE.equals(u.emailConfirmed)
                      && GmailAccounts.allowed(u.email))
          .isPresent())
        throw new IllegalArgumentException(
            "A recipient is no longer eligible. Refresh the recipient list.");
      if (db.jdbc()
              .queryForObject(
                  "SELECT COUNT(*) FROM \"UserAccessNotifications\" WHERE \"UserId\"=? AND"
                      + " \"NotificationType\"='ApiIssueMessage' AND \"Body\"=? AND"
                      + " \"DeliveryStatus\" IN ('Pending','Sending','Delivered')",
                  Long.class,
                  id,
                  body.toString())
          > 0) continue;
      accounts.queueNotification(
          id,
          sender,
          "GSO Web Services - API issues (" + ids.size() + ")",
          body.toString(),
          "ApiIssueMessage");
      count++;
    }
    return count;
  }
}
