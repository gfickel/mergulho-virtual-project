from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from api.api import api_router

app = FastAPI()

app.mount("/static", StaticFiles(directory="static"), name="static")

app.include_router(api_router)

@app.get("/")
async def root():
    return {"message": "API do Mergulho Virtual", "version": 0.1}
