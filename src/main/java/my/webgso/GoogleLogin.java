package my.webgso;

import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.oauth2.client.CommonOAuth2Provider;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.client.registration.ClientRegistrationRepository;
import org.springframework.security.oauth2.client.registration.InMemoryClientRegistrationRepository;
import org.springframework.security.oauth2.core.oidc.user.OidcUser;
import org.springframework.security.web.context.HttpSessionSecurityContextRepository;
import org.springframework.stereotype.Component;

@Component
public class GoogleLogin {
  private final GoogleAccounts googleAccounts;
  private final Accounts accounts;

  public GoogleLogin(GoogleAccounts googleAccounts, Accounts accounts) {
    this.googleAccounts = googleAccounts;
    this.accounts = accounts;
  }

  public void success(HttpServletRequest request, HttpServletResponse response, Authentication auth)
      throws IOException {
    String result;
    try {
      var identity = (OidcUser) auth.getPrincipal();
      var user =
          googleAccounts.resolve(
              identity.getSubject(),
              identity.getEmail(),
              identity.getEmailVerified(),
              identity.getFullName());
      if ("Approved".equals(user.accessStatus)) {
        var context = SecurityContextHolder.createEmptyContext();
        context.setAuthentication(accounts.token(user));
        SecurityContextHolder.setContext(context);
        new HttpSessionSecurityContextRepository().saveContext(context, request, response);
        response.sendRedirect("/");
        return;
      }
      result = "pending";
    } catch (GoogleAccounts.ExistingEmailException e) {
      result = "existing";
    } catch (RuntimeException e) {
      result = "error";
    }
    clear(request);
    response.sendRedirect("/login?google=" + result);
  }

  public static void clear(HttpServletRequest request) {
    SecurityContextHolder.clearContext();
    if (request.getSession(false) != null) request.getSession(false).invalidate();
  }

  @Configuration
  @ConditionalOnProperty(name = "webgso.google.enabled", havingValue = "true")
  static class Registration {
    @Bean
    ClientRegistrationRepository googleClients(
        @Value("${webgso.google.client-id}") String id,
        @Value("${webgso.google.client-secret}") String secret,
        @Value("${webgso.google.redirect-uri}") String redirect) {
      if (id.isBlank() || secret.isBlank())
        throw new IllegalStateException(
            "Configure Google OAuth credentials before enabling Google login.");
      return new InMemoryClientRegistrationRepository(
          CommonOAuth2Provider.GOOGLE
              .getBuilder("google")
              .clientId(id)
              .clientSecret(secret)
              .scope("openid", "profile", "email")
              .redirectUri(redirect)
              .build());
    }
  }
}
