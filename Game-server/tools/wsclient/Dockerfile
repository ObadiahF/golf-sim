FROM python:3.12-slim
RUN pip install --no-cache-dir websockets==13.1
WORKDIR /app
COPY wsclient.py .
ENTRYPOINT ["python", "-u", "wsclient.py"]
