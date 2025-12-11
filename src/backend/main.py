from fastapi import FastAPI

app = FastAPI()


@app.get("/")
async def root():
    return {"message": "Virtual Diving API"}

@app.get("/sightings")
async def sightings():
    return {"message": "Placeholder for shark sightings info"}

@app.get("/telemetry")
async def telemetry():
    return {"message": "Placeholder for shark monitoring"}
