package my.webgso.ui;

import static my.webgso.ui.Html.el;
import static my.webgso.ui.PageContext.*;

import my.webgso.ui.Html.Element;

/** A dismissible error message; closing it keeps the current form and its input. */
final class RequestErrorPopup {
  private RequestErrorPopup() {}

  static Element render(String message, boolean returnLink) {
    return el(
            "dialog",
            el("p", Branding.REPOSITORY).cls("section-label"),
            el("h2", "Request could not be completed").attr("id", "request-error-title"),
            el("p", message).attr("id", "request-error-message").attr("role", "alert"),
            el(
                    "form",
                    returnLink ? action("Return to web services", "/", false) : null,
                    button("OK", true).flag("autofocus", true))
                .attr("method", "dialog")
                .cls("java-actions"))
        .cls("api-test-popup")
        .flag("open", true)
        .attr("aria-labelledby", "request-error-title")
        .attr("aria-describedby", "request-error-message");
  }
}
