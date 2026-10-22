package my.webgso;

import jakarta.servlet.http.HttpServletRequest;
import java.time.DateTimeException;
import java.util.*;
import org.slf4j.*;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.*;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.servlet.ModelAndView;

@ControllerAdvice
public class Errors {
  private static final Logger log = LoggerFactory.getLogger(Errors.class);

  @ExceptionHandler({
    IllegalArgumentException.class,
    NoSuchElementException.class,
    DateTimeException.class,
    DataIntegrityViolationException.class,
    AccessDeniedException.class
  })
  public Object expected(Exception error, HttpServletRequest request) {
    int status = error instanceof AccessDeniedException ? 403 : 400;
    String message =
        error instanceof DataIntegrityViolationException
            ? "A duplicate or invalid linked record was found. Check the submitted configuration."
            : Objects.toString(error.getMessage(), "Invalid request");
    if (request.getRequestURI().startsWith("/api/"))
      return ResponseEntity.status(status).body(Map.of("error", message));
    var view = new ModelAndView("error-page");
    view.setStatus(HttpStatus.valueOf(status));
    view.addObject("message", message);
    return view;
  }

  @ExceptionHandler(Exception.class)
  public Object unexpected(Exception error, HttpServletRequest request) {
    String reference = UUID.randomUUID().toString();
    log.error("Request failed; reference {}", reference, error);
    String message = "The request could not be completed. Reference: " + reference;
    if (request.getRequestURI().startsWith("/api/"))
      return ResponseEntity.status(500).body(Map.of("error", message));
    var view = new ModelAndView("error-page");
    view.setStatus(HttpStatus.INTERNAL_SERVER_ERROR);
    view.addObject("message", message);
    return view;
  }
}
