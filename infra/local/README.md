# infra/local

docker-compose stack for running Sentinel on one machine: PostgreSQL, Redis,
a Kafka-compatible broker and the services. Start it with:

```sh
docker compose -f infra/local/docker-compose.yml up -d --wait
```
