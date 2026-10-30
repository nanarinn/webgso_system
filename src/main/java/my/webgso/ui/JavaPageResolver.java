package my.webgso.ui;

import jakarta.servlet.http.*;
import java.util.*;
import org.springframework.core.Ordered;
import org.springframework.stereotype.Component;
import org.springframework.web.servlet.*;
import org.springframework.web.servlet.view.AbstractView;

/** MVC views are built from Java components, without an HTML template engine. */
@Component
public final class JavaPageResolver implements ViewResolver, Ordered {
  private static final Set<String> PAGES =
      Set.of(
          "login",
          "register",
          "error-page",
          "dashboard",
          "admin-overview",
          "analytics",
          "collections",
          "collection",
          "reports",
          "resource",
          "edit",
          "users",
          "profile",
          "api-history",
          "api-retest-progress",
          "api-quick-test",
          "batch-progress");

  @Override
  public int getOrder() {
    return Ordered.HIGHEST_PRECEDENCE + 10;
  }

  @Override
  public View resolveViewName(String name, Locale locale) {
    if (!PAGES.contains(name)) return null;
    return new AbstractView() {
      {
        setContentType("text/html;charset=UTF-8");
      }

      @Override
      protected void renderMergedOutputModel(
          Map<String, Object> model, HttpServletRequest request, HttpServletResponse response)
          throws Exception {
        response.setContentType(getContentType());
        response.setCharacterEncoding("UTF-8");
        response.setHeader("Cache-Control", "no-store");
        var context = new PageContext(model, request);
        response.getWriter().write(JavaPages.render(name, context));
      }
    };
  }
}
