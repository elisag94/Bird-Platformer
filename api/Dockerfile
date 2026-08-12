FROM python:3.12-slim

# PYTHONUNBUFFERED matters more than it looks: without it, Python buffers
# stdout, so `kubectl logs` shows nothing until the buffer flushes. Debugging a
# crash with no log output is a miserable afternoon.
ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1

WORKDIR /app

# requirements.txt is copied and installed BEFORE the source. Docker caches
# each layer, so editing app.py does not re-run pip install. Copy the whole
# directory first and every code change costs a full dependency download.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Run as a non-root user. Not strictly required by this cluster, but it is the
# first thing a security-minded reviewer looks for, and switching later is
# harder than starting this way.
RUN useradd --create-home --uid 10001 appuser \
    && chown -R appuser:appuser /app
USER appuser

# 8080 rather than 80: ports below 1024 need root to bind.
EXPOSE 8080

# gunicorn, not `flask run`. The dev server is single-threaded and prints a
# warning telling you not to use it in production.
#   -w 2            two worker processes
#   --timeout 30    kill a worker wedged on a slow query
#   --access-logfile -   access logs to stdout, where kubectl logs can see them
CMD ["gunicorn", \
     "--bind", "0.0.0.0:8080", \
     "--workers", "2", \
     "--timeout", "30", \
     "--access-logfile", "-", \
     "app:app"]