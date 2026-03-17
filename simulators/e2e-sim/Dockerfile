FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY e2e_simulator.py .

EXPOSE 161/udp
EXPOSE 8080/tcp

CMD ["python", "-u", "e2e_simulator.py"]
