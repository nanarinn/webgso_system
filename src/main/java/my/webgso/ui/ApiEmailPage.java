package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ApiIssueEmails;
import my.webgso.Models.*;
import my.webgso.ui.Html.Element;

final class ApiEmailPage {
  static Element render(PageContext c) {
    var issues = el("div").cls("api-email-options");
    for (var issue : c.<ApiIssueEmails.Issue>list("emailIssues")) {
      var test = issue.test();
      issues.add(
          el(
              "label",
              input("issueIds", "checkbox", test.id()),
              el(
                  "span",
                  el("strong", issue.plant() + " · " + test.dataset()),
                  el(
                      "small",
                      issue.reason()
                          + " · "
                          + test.testedAt().atZoneSameInstant(my.webgso.Policies.LOCAL)))));
    }
    if (c.list("emailIssues").isEmpty())
      issues.add(
          el(
                  "p",
                  "No failed or empty API results. Test your configured APIs to check their latest"
                      + " status.")
              .cls("muted"));
    var recipients = el("div").cls("api-email-options");
    for (var user : c.<ApplicationUser>list("emailRecipients"))
      recipients.add(
          el(
              "label",
              input("recipientIds", "checkbox", user.id),
              el("span", el("strong", user.email), el("small", user.displayName))));
    if (c.list("emailRecipients").isEmpty())
      recipients.add(el("p", "No active, email-verified recipients are available."));
    var rows = el("tbody");
    for (var note : c.<UserAccessNotification>list("emailDeliveries"))
      rows.add(
          el(
              "tr",
              el("td", note.email),
              el("td", note.subject),
              el("td", note.deliveryStatus),
              el("td", note.createdAt.atZoneSameInstant(my.webgso.Policies.LOCAL))));
    return el(
        "section",
        heading(
            "Notifications",
            "API email alerts",
            "Send failed or empty API test results to registered users.",
            action("Plant Config", "/admin/lss-plants", false)),
        c.text("emailNotice").isBlank()
            ? null
            : el("p", c.text("emailNotice")).cls("account-tools-notice").attr("role", "status"),
        c.text("emailError").isBlank()
            ? null
            : el("p", c.text("emailError")).cls("profile-error").attr("role", "alert"),
        c.post(
            "/admin/api-email",
            "panel api-email-form",
            el(
                    "div",
                    el(
                        "fieldset",
                        el("legend", "1 · Select API issues"),
                        el(
                            "p",
                            "Only the latest failed or empty results for configured APIs are"
                                + " listed."),
                        issues),
                    el(
                        "fieldset",
                        el("legend", "2 · Select recipients"),
                        el(
                            "p",
                            "Only active, verified Gmail accounts registered in GSO can receive"
                                + " this report."),
                        recipients))
                .cls("api-email-grid"),
            el(
                    "footer",
                    el(
                        "p",
                        "Each recipient receives a separate email. Duplicate reports are not sent"
                            + " again."),
                    button("Send email", true))
                .cls("api-email-footer")),
        el(
                "article",
                el("h2", "Recent email deliveries"),
                el(
                    "p",
                    "Pending messages are sent while the GSO server is running. Refresh to check"
                        + " the latest delivery status."),
                el(
                        "div",
                        el(
                            "table",
                            el(
                                "thead",
                                el(
                                    "tr",
                                    el("th", "Recipient"),
                                    el("th", "Subject"),
                                    el("th", "Status"),
                                    el("th", "Queued at (MYT)"))),
                            rows))
                    .cls("api-email-table"),
                c.list("emailDeliveries").isEmpty() ? el("p", "No reports sent yet.") : null)
            .cls("panel account-tools-card"));
  }
}
