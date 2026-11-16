package my.webgso;

import jakarta.servlet.*;
import jakarta.servlet.http.*;
import java.io.IOException;
import java.util.Objects;
import org.springframework.context.annotation.*;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.AnonymousAuthenticationFilter;
import org.springframework.security.web.csrf.CsrfException;
import org.springframework.web.filter.OncePerRequestFilter;

@Configuration
public class SecurityConfiguration {
  @Bean
  SecurityFilterChain security(HttpSecurity http, Accounts accounts, Store db) throws Exception {
    http.authenticationProvider(accounts)
        .authorizeHttpRequests(
            auth ->
                auth.requestMatchers(
                        "/oauth2/**", "/login/oauth2/**", "/account/profile/google/link")
                    .denyAll()
                    .requestMatchers(
                        "/login",
                        "/register",
                        "/account/register",
                        "/account/google",
                        "/account/verify-email",
                        "/account/resend-verification",
                        "/account/forgot-password",
                        "/account/reset-password",
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
                    .requestMatchers("/", "/account/profile", "/account/profile/**",
                        "/account/first-password", "/user/lss", "/user/plant-config", "/user/ddq-test", "/user/mmf-test")
                    .hasAnyRole("Admin", "User")
                    .requestMatchers("/admin/**", "/api/admin/**")
                    .hasRole("Admin")
                    .anyRequest()
                    .hasRole("Admin"))
        .formLogin(
            form ->
                form.loginPage("/login")
                    .loginProcessingUrl("/account/login")
                    .usernameParameter("identifier")
                    .successHandler((request, response, authentication) -> {
                    var principal = (Accounts.Principal) authentication.getPrincipal();
                    response.sendRedirect(request.getContextPath() + (accounts.requiresInitialPassword(principal.id()) ? "/account/first-password" : "/"));
                  })
                    .failureHandler((request, response, error) -> response.sendRedirect(
                        request.getContextPath() + (error instanceof DeletedAccountException
                            ? "/login?accountDeleted=true" : "/login?error=true")))
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
                          // Reject stale browser forms without replaying the POST or bypassing
                          // CSRF. API clients and other protected routes retain their 403 response.
                          String path = request.getRequestURI().substring(request.getContextPath().length());
                          boolean loginForm = "/account/login".equals(path);
                          boolean apiTestForm = java.util.Set.of(
                              "/admin/ddq-test", "/admin/mmf-test", "/admin/api-quick-test",
                              "/user/ddq-test", "/user/mmf-test").contains(path);
                          if (error instanceof CsrfException
                              && "POST".equals(request.getMethod())
                              && (loginForm || apiTestForm)) {
                            response.setHeader("Cache-Control", "no-store");
                            response.sendRedirect(request.getContextPath() + "/login?reason="
                                + (loginForm ? "form-expired" : "request-expired"));
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
                  String path = request.getRequestURI().substring(request.getContextPath().length());
                  if (accounts.requiresInitialPassword(principal.id())
                      && !path.equals("/account/first-password") && !path.equals("/account/logout")
                      && !path.startsWith("/styles/") && !path.startsWith("/scripts/")
                      && !path.startsWith("/bootstrap/") && !path.matches("/[^/]+\\.(png|svg)") && !path.equals("/error")) {
                    if (path.startsWith("/api/")) response.sendError(403);
                    else response.sendRedirect(request.getContextPath() + "/account/first-password");
                    return;
                  }
                  SecurityContextHolder.getContext().setAuthentication(accounts.token(user.get()));
                }
                chain.doFilter(request, response);
              }
            },
            AnonymousAuthenticationFilter.class);
    return http.build();
  }
}
