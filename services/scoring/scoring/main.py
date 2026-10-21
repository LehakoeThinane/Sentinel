"""FastAPI app: health endpoints, with the Kafka worker running alongside."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI, Response, status

from scoring import logs
from scoring.worker import ScoringWorker

logs.configure()


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    worker = ScoringWorker.from_env()
    worker.start()
    app.state.worker = worker
    try:
        yield
    finally:
        worker.stop()


app = FastAPI(title="Sentinel scoring", lifespan=lifespan)


@app.get("/health/live")
def live() -> dict[str, str]:
    return {"status": "live"}


@app.get("/health/ready")
def ready(response: Response) -> dict[str, str]:
    if app.state.worker.ready:
        return {"status": "ready"}
    response.status_code = status.HTTP_503_SERVICE_UNAVAILABLE
    return {"status": "starting"}
