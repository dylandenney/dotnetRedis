# Redis Lock .NET Application

This .NET application demonstrates how to use Redis for distributed locking and PostgreSQL for database operations. The application ensures that only one instance writes data to the database at a time, preventing duplicate entries and maintaining data integrity.

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Running the Application](#running-the-application)
- [Configuration](#configuration)
- [Project Structure](#project-structure)
- [Important Notes](#important-notes)
- [Contributing](#contributing)
- [License](#license)

## Overview

The application uses Redis to implement a distributed lock, ensuring that only one instance of the application writes to the PostgreSQL database at a time. It periodically attempts to write a predefined data entry to the database and prevents duplicate entries by checking the state in both Redis and PostgreSQL.

## Prerequisites

- .NET SDK 5.0 or higher
- Docker
- Kubernetes
- Redis server
- PostgreSQL server
- Kubernetes cluster with appropriate configurations

## Setup

### Step 1: Clone the Repository

git clone https://github.com/yourusername/redis-lock-dotnet-app.git
cd redis-lock-dotnet-app

### Step 2: Build the Docker Image

docker build -t your-dockerhub-username/redislock:latest .

### Step 3: Push the Docker Image

docker push your-dockerhub-username/redislock:latest

### Step 4: Deploy to Kubernetes

Create a Kubernetes deployment file (dotnetapp.yml) and apply it:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis-lock-app
  namespace: poc
spec:
  replicas: 4
  selector:
    matchLabels:
      app: redis-lock-app
  template:
    metadata:
      labels:
        app: redis-lock-app
    spec:
      containers:
      - name: redis-lock-app
        image: your-dockerhub-username/redislock:latest
        imagePullPolicy: Always
        env:
        - name: REDIS_HOST
          value: "redis.poc.svc.cluster.local"
        - name: REDIS_PORT
          value: "6379"
        - name: PG_HOST
          value: "my-postgresql.poc.svc.cluster.local"
        - name: PG_DB
          value: "mydb"
        - name: PG_USER
          value: "myuser"
        - name: PG_PASSWORD
          valueFrom:
            secretKeyRef:
              name: my-postgresql
              key: postgres-password
        - name: REDIS_PASSWORD
          valueFrom:
            secretKeyRef:
              name: redis-password
              key: password
      imagePullSecrets:
```
# Apply the deployment
```bash
kubectl apply -f dotnetapp.yml
```
# Running the Application
# Start the Application
# The application will start automatically upon deployment.

# Check the Logs
# Monitor the application logs to ensure it is functioning correctly.
kubectl logs -n poc <pod-name>

# Database Verification
# Ensure data is being written to your PostgreSQL database by checking the my_table table.
SELECT * FROM my_table;

# Configuration
# The application uses environment variables to configure connections to Redis and PostgreSQL. 
# You can set these variables in your Kubernetes deployment or locally for testing.

# Environment Variables
# REDIS_HOST: The host address of the Redis server.
# REDIS_PORT: The port number for the Redis server.
# REDIS_PASSWORD: The password for Redis authentication.
# PG_HOST: The host address of the PostgreSQL server.
# PG_DB: The PostgreSQL database name.
# PG_USER: The PostgreSQL username.
# PG_PASSWORD: The PostgreSQL password.

# Redis and PostgreSQL Secrets
# Create Kubernetes secrets for storing sensitive information such as passwords:
kubectl create secret generic redis-password --from-literal=password='YourRedisPassword' -n poc
kubectl create secret generic my-postgresql --from-literal=postgres-password='YourPostgresPassword' -n poc

# Project Structure
# redis-lock-dotnet-app/
# ├── Dockerfile            # Dockerfile for building the application image
# ├── Program.cs            # Main application logic
# ├── RedisLockApp.csproj   # Project file
# └── README.md             # This README file

# Important Notes
# Ensure that your Redis and PostgreSQL servers are accessible from the Kubernetes cluster.
# Adjust the environment variables and configurations to match your deployment requirements.
# The application logs provide detailed information on operations and errors.

# Contributing
# We welcome contributions to improve this project. Feel free to open issues or submit pull requests on GitHub.

# License
# This project is licensed under the MIT License. See the LICENSE file for details.
