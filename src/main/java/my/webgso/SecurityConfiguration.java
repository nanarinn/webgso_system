package my.webgso;

import jakarta.servlet.*;
import jakarta.servlet.http.*;
import java.io.IOException;
import java.util.Objects;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.context.annotation.*;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.client.registration.ClientRegistrationRepository;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.AnonymousAuthenticationFilter;
import org.springframework.security.web.csrf.CsrfException;
import org.springframework.web.filter.OncePerRequestFilter;

@Configuration
public class SecurityConfiguration {
  @Bean
  SecurityFilterChain security(
      HttpSecurity http,
      Accounts accounts,
      Store db,
      ObjectProvider<ClientRegistrationRepository> clients,
      GoogleLogin google)
      throws Exception {
    http.authenticationProvider(accounts)
        .authorizeHttpRequests(
            auth ->
                auth.requestMatchers(
                        "/login",
                        "/register",
                        "/account/register",
                        "/account/google",
                        "/account/verify-email",
                        "/account/resend-verification",
                        "/account/forgot-password",
                        "/account/reset-password",
                        "/oauth2/**",
                        "/login/oauth2/**",
                        "/styles/**",
                        "/bootstrap/**",
                        "/scripts/**",
                        "/*.png",
                        "/*.svg",
                        "/health",
                        "/api/test-data/mmf/**",
                        "/error")
                    .permitAll()
                    .requestMatchers("/admin/users", "/admin/users/**")
                    .denyAll()
                    .requestMatchers("/admin/**", "/api/admin/**")
                    .hasRole("Admin")
                    .anyRequest()
                    .hasRole("Admin"))
        .formLogin(
            form ->
                form.loginPage("/login")
                    .loginProcessingUrl("/account/login")
                    .usernameParameter("identifier")
                    .defaultSuccessUrl("/", true)
                    .failureUrl("/login?error=true")
                    .permitAll())
        .logout(
            logout ->
                logout
                    .logoutUrl("/account/logout")
                    .logoutSuccessUrl("/login?logout=true")
                    .invalidateHttpSession(true)
                    .deleteCookies("JSESSIONID"))
        .exceptionHandling(
            ex ->
                ex.accessDeniedHandler(
                        (request, response, error) -> {
                          // Reject stale login submissions without replaying credentials or
                          // bypassing
                          // CSRF. A fresh GET renders a new session-bound token.
                          if (error instanceof CsrfException
                              && "POST".equals(request.getMethod())
                              && (request.getContextPath() + "/account/login")
                                  .equals(request.getRequestURI())) {
                            response.sendRedirect(
                                request.getContextPath() + "/login?reason=form-expired");
                          } else {
                            response.sendError(HttpServletResponse.SC_FORBIDDEN);
                          }
                        })
                    .authenticationEntryPoint(
                        (request, response, error) -> {
                          if (request.getRequestURI().startsWith("/api/")) response.sendError(401);
                          else response.sendRedirect("/login");
                        }))
        .headers(
            headers ->
                headers.contentSecurityPolicy(
                    csp ->
                        csp.policyDirectives(
                            "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src"
                                + " 'self'; object-src 'none'; base-uri 'self'; frame-ancestors"
                                + " 'self'; form-action 'self'")))
        .addFilterBefore(
            new OncePerRequestFilter() {
              @Override
              protected void doFilterInternal(
                  HttpServletRequest request, HttpServletResponse response, FilterChain chain)
                  throws ServletException, IOException {
                var auth = SecurityContextHolder.getContext().getAuthentication();
                if (auth != null && auth.getPrincipal() instanceof Accounts.Principal principal) {
                  var user = db.find(Models.ApplicationUser.class, principal.id());
                  if (user.isEmpty()
                      || !"Approved".equals(user.get().accessStatus)
                      || !Objects.equals(principal.stamp(), user.get().securityStamp)) {
                    SecurityContextHolder.clearContext();
                    if (request.getSession(false) != null) request.getSession(false).invalidate();
                    if (request.getRequestURI().startsWith("/api/")) response.sendError(401);
                    else response.sendRedirect("/login?reason=session-timeout");
                    return;
                  }
                  SecurityContextHolder.getContext().setAuthentication(accounts.token(user.get()));
                }
                chain.doFilter(request, response);
              }
            },
            AnonymousAuthenticationFilter.class);
    if (clients.getIfAvailable() != null) {
      http.oauth2Login(
          oauth ->
              oauth
                  .loginPage("/login")
                  .clientRegistrationRepository(clients.getObject())
                  .authorizationEndpoint(
                      endpoint ->
                          endpoint.authorizationRequestResolver(
                              google.resolver(clients.getObject())))
                  .successHandler(google::success)
                  .failureHandler(
                      (request, response, error) -> {
                        GoogleLogin.clear(request);
                        response.sendRedirect("/login?google=error");
                      }));
    }
    return http.build();
  }
}
