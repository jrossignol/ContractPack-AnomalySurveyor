using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using Contracts;
using ContractConfigurator;
using ContractConfigurator.Parameters;

namespace AnomalySurveyor
{
    public class MonolithParameter : ContractConfiguratorParameter, ParameterDelegateContainer
    {
        public class VelocityHandler : MonoBehaviour
        {
            public MonolithParameter param;
            private int lastAdjustment = 0;

            void Start()
            {
                LoggingUtil.LogVerbose(typeof(MonolithParameter), "VelocityHandler startup");
            }

            void FixedUpdate()
            {
                if (param != null && param.starJeb != null)
                {
                    if (param.velocity != null)
                    {
                        param.starJeb.SetPosition(param.starJeb.transform.position + (param.velocity.Value * Time.fixedDeltaTime));
                    }
                    else if (param.Destination != null)
                    {
                        Vector3 direction = (param.starJeb.transform.position - param.Destination.Value).normalized;

                        float t = Time.fixedTime - param.stepTime;
                        float x = t / 8.0f - 1.0f;
                        if (t + Time.fixedDeltaTime * 2.0f > 8.0f)
                        {
                            x = 0.0f;
                        }

                        if (param.starJeb.altitude < param.starJeb.terrainAltitude + 10.0f)
                        {
                            CelestialBody body = param.DestinationBody;
                            double lat = body.GetLatitude(param.starJeb.transform.position);
                            double lon = body.GetLongitude(param.starJeb.transform.position);
                            param.starJeb.SetPosition(body.GetWorldSurfacePosition(lat, lon, param.starJeb.terrainAltitude + 10.0f));
                        }
                        else
                        {
                            param.starJeb.SetPosition(param.Destination.Value + direction * param.startDistance * x * x);
                        }
                    }

                    if (param.Destination != null)
                    {
                        // Adjust the velocity, but only every frame - adjusting this too frequently
                        // seems to cause the velocity to resonate back and forth
                        if (Time.frameCount != lastAdjustment)
                        {
                            lastAdjustment = Time.frameCount;
                            param.starJeb.ChangeWorldVelocity(-param.starJeb.GetSrfVelocity());

                            // Keep us safe!
                            foreach (Part p in param.starJeb.parts)
                            {
                                p.crashTolerance = 10000000.0f;
                            }
                            CheatOptions.NoCrashDamage = true;
                        }
                    }
                }
            }
        }

        private const int ASTEROID_COUNT = 16;

        protected enum MonolithState
        {
            STARTED,
            EVA,
            FULL_OF_STARS1,
            FULL_OF_STARS2,
            FULL_OF_STARS3,
            FULL_OF_STARS4,
            FULL_OF_STARS5,
            FULL_OF_STARS_DRES1,
            FULL_OF_STARS_DRES2,
            FULL_OF_STARS_DRES3,
            FULL_OF_STARS_DUNA1,
            FULL_OF_STARS_DUNA2,
            FULL_OF_STARS_DUNA3,
            FULL_OF_STARS_EELOO1,
            FULL_OF_STARS_EELOO2,
            FULL_OF_STARS_EELOO3,
            // One day, I may try to get this stuff working again, but it's just so buggy!
            //FULL_OF_STARS_EVE1,
            //FULL_OF_STARS_EVE2,
            //FULL_OF_STARS_EVE3,
            //FULL_OF_STARS_EVE4,
            //FULL_OF_STARS_EVE5,
            //FULL_OF_STARS_EVE6,
            FULL_OF_STARS_KERBIN1,
            FULL_OF_STARS_KERBIN2,
            FULL_OF_STARS_KERBIN3,
            FULL_OF_STARS_KERBIN4,
            FULL_OF_STARS_FINAL,
            FINISHED,
            FULL_OF_STARS_EVE1,
            FULL_OF_STARS_EVE2,
            FULL_OF_STARS_EVE3,
            FULL_OF_STARS_EVE4,
            FULL_OF_STARS_EVE5,
            FULL_OF_STARS_EVE6,
        }
        public bool ChildChanged { get; set; }

        private const string STARJEB_MESSAGE =
@"And so it was that {0} became an immortal being known as ""The Star Jeb"".  After the Star Jeb's transcendence, the Jool monolith disappeared - no one knows if it will ever reappear.

As for the Star Jeb, they have the ability to advance Kerbal science and the Kerbal Space Program to great new heights.  However, they've done absolutely nothing.  In fact, rumor has it that the Star Jeb hasn’t even called their mother.";

        private const float MONOLITH_DRAW_DISTANCE = 500000;
        private const float MONOLITH_DISCOVERY_DISTANCE = 50000;
        private const float MONOLITH_TOO_CLOSE = 2000;

        private Vessel monolith = null;
        public Vessel starJeb = null;
        public Vessel candidate = null;
        public string starJebName = "";
        public string candidateName = "";
        private bool monolithDiscovered = false;
        private MonolithState currentState = MonolithState.STARTED;
        private float stepTime = 0;
        private float distance;
        private VelocityHandler velHdlr;
        private ConfigNode progressTreeBackup = null;
        private double eveLatitude, eveLongitude;

        public Vector3? velocity = null;
        public Vector3? Destination
        {
            get
            {
                if (currentState >= MonolithState.FULL_OF_STARS_EVE1 && currentState <= MonolithState.FULL_OF_STARS_EVE3)
                {
                    return selectEvePoint();
                }
                return null;
            }
        }
        public CelestialBody DestinationBody
        {
            get
            {
                if (currentState >= MonolithState.FULL_OF_STARS_EVE1 && currentState <= MonolithState.FULL_OF_STARS_EVE3)
                {
                    return FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                }
                return null;
            }
        }
        public float startDistance;

        public MonolithParameter()
            : base()
        {
        }

        protected override string GetParameterTitle()
        {
            return "Investigate the monolith";
        }

        protected void CreateDelegates()
        {
            if (ParameterCount < 1)
            {
                LoggingUtil.LogVerbose(this, "Adding EVA parameter...");
                AddParameter(new ParameterDelegate<MonolithParameter>("Send a Kerbal on EVA", x => CheckParameters(MonolithState.STARTED)));
            }

            if (ParameterCount < 2 && currentState >= MonolithState.EVA)
            {
                LoggingUtil.LogVerbose(this, "Adding approach parameter...");
                AddParameter(new ParameterDelegate<MonolithParameter>("Approach the monolith with " + candidateName,
                    x => CheckParameters(MonolithState.EVA)));
            }

            if (ParameterCount < 3 && currentState >= MonolithState.FULL_OF_STARS1)
            {
                LoggingUtil.LogVerbose(this, "Adding 'full of stars' parameter...");
                AddParameter(new ParameterDelegate<MonolithParameter>("...it's full of stars!", x => CheckParameters(MonolithState.FULL_OF_STARS_FINAL)));
            }
        }

        protected bool CheckParameters(MonolithState paramState)
        {
            if (paramState < currentState)
            {
                return true;
            }

            // StarJeb not active vessel
            if (starJeb != null && FlightGlobals.ActiveVessel != starJeb ||
                candidate != null && FlightGlobals.ActiveVessel != candidate)
            {
                stepTime = Time.fixedTime;
                return false;
            }

            // Create the velocity change handler
            if (velHdlr == null)
            {
                LoggingUtil.LogVerbose(this, "Adding VelocityHandler");
                velHdlr = MapView.MapCamera.gameObject.AddComponent<VelocityHandler>();
                velHdlr.param = this;
            }

            switch (currentState)
            {
                case MonolithState.STARTED:
                    // Look for an eva
                    if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.vesselType == VesselType.EVA)
                    {
                        candidate = FlightGlobals.ActiveVessel;
                        candidateName = candidate.vesselName;
                        LoggingUtil.LogVerbose(this, "Got an eva, starJeb = " + candidate.vesselName);
                        return true;
                    }
                    return false;
                case MonolithState.EVA:
                    {
                        Vessel discovery = ContractVesselTracker.Instance.GetAssociatedVessel("Discovery One");
                        float discoveryDistance = discovery == null ? 10000 : Vector3.Distance(discovery.transform.position, candidate.transform.position);

                        if (distance < 10000 && discoveryDistance > distance && Time.fixedTime - stepTime > 10.0f || distance < MONOLITH_TOO_CLOSE)
                        {
                            starJeb = candidate;
                            starJebName = candidateName;
                            candidate = null;
                            return true;
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS1:
                    {
                        // Backup progress tracking
                        progressTreeBackup = new ConfigNode("PROGRESS_TREE_BACKUP");
                        ProgressTracking.Instance.OnSave(progressTreeBackup);

                        // Give the first kick away from Jool - this one using regular velocity change
                        CelestialBody jool = FlightGlobals.Bodies.Where(b => b.name == "Jool").First();

                        // Find closest point on the jool-monolith line, and throw us away from that (so we don't hit either)
                        Vector3 line = monolith.transform.position - jool.transform.position;
                        float t = Vector3.Dot(line, (starJeb.transform.position - jool.transform.position)) / Vector3.Dot(line, line);
                        Vector3 closest = jool.transform.position + line * t;

                        velocity = (starJeb.transform.position - (t > 1.0 ? jool.transform.position : closest)).normalized;
                        velocity += new Vector3(0.0f, 0.1f, 0.0f);
                        velocity *= 15000;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);
                        nextState();

                        // Camera to target jool
                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition((starJeb.transform.position - jool.transform.position).normalized * 25.0f);
                    }
                    return false;
                case MonolithState.FULL_OF_STARS2:
                    if (Time.fixedTime - stepTime > 4.0f)
                    {
                        // Give the second kick away from Jool - these using anti-kraken velocity change
                        CelestialBody jool = FlightGlobals.Bodies.Where(b => b.name == "Jool").First();
                        velocity = (starJeb.transform.position - jool.transform.position).normalized;
                        velocity += new Vector3(0.0f, 0.1f, 0.0f);
                        velocity *= 1500000;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS3:
                    if (Time.fixedTime - stepTime > 3.0f)
                    {
                        // Give the third kick away from Jool
                        CelestialBody jool = FlightGlobals.Bodies.Where(b => b.name == "Jool").First();
                        velocity = (starJeb.transform.position - jool.transform.position).normalized;
                        velocity *= 20000000;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS4:
                    if (Time.fixedTime - stepTime > 2.0f)
                    {
                        // Give the fourth and final kick away from Jool
                        CelestialBody jool = FlightGlobals.Bodies.Where(b => b.name == "Jool").First();
                        velocity = (starJeb.transform.position - jool.transform.position).normalized;
                        velocity *= 200000000;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS5:
                    if (Time.fixedTime - stepTime > 2.0f)
                    {
                        // Move along
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DRES1:
                    {
                        // Visit Dres
                        CelestialBody dres = FlightGlobals.Bodies.Where(b => b.name == "Dres").First();

                        // Determine which side the sun is on - makes for a better show
                        CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();
                        Vector3 sunnySide = sun.transform.position - dres.transform.position;
                        sunnySide.x = 0.0f;
                        sunnySide.y = 1; // Move across the top of the planet
                        sunnySide.z = Math.Sign(sunnySide.z);

                        // Set position for starjeb
                        float distance = 4.0f * (float)dres.Radius;
                        starJeb.SetPosition(dres.transform.position + new Vector3(distance, (float)dres.Radius, (float)dres.Radius * sunnySide.z));

                        velocity = (dres.transform.position - starJeb.transform.position + sunnySide * ((float)dres.Radius)).normalized;
                        velocity *= distance / 3.0f;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);
                        starJeb.SetWorldVelocity(dres.getRFrmVel(starJeb.transform.position));
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DRES2:
                    {
                        // Camera to target Dres - do this on a seperate update to allow KSP to catch up
                        CelestialBody dres = FlightGlobals.Bodies.Where(b => b.name == "Dres").First();
                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + (starJeb.transform.position - dres.transform.position).normalized * 10.0f);

                        // Make sure that the camera gets fixed
                        if (Time.fixedTime - stepTime > 0.1f)
                        {
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DRES3:
                    if (Time.fixedTime - stepTime > 5.5f)
                    {
                        // Done with Dres
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DUNA1:
                    {
                        // Start between the sun and Duna
                        CelestialBody duna = FlightGlobals.Bodies.Where(b => b.name == "Duna").First();
                        CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();
                        Vector3 sunnySide = sun.transform.position - duna.transform.position;
                        sunnySide.Normalize();
                                                
                        // Set us up a nice 4 radiuses away...
                        float distance = 4.0f * (float)duna.Radius;
                        starJeb.SetPosition(duna.transform.position + sunnySide * distance);

                        // Go straight at Duna
                        velocity = (duna.transform.position - starJeb.transform.position).normalized;
                        velocity *= distance / 3.0f;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);

                        // Now offset him down so he doesn't actually hit Duna...
                        starJeb.SetPosition(starJeb.transform.position + new Vector3(0.0f, -((float)duna.Radius + 45000), 0.0f));
                        starJeb.SetWorldVelocity(duna.getRFrmVel(starJeb.transform.position));

                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DUNA2:
                    {
                        // Camera to target Duna - do this on a seperate update to allow KSP to catch up
                        CelestialBody duna = FlightGlobals.Bodies.Where(b => b.name == "Duna").First();
                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + (starJeb.transform.position - duna.transform.position).normalized * 25.0f);

                        // Make sure that the camera gets fixed
                        if (Time.fixedTime - stepTime > 0.1f)
                        {
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_DUNA3:
                    if (Time.fixedTime - stepTime > 5.5f)
                    {
                        // Done with Duna
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EELOO1:
                    {
                        // Start perpendicular to the sun and Eeloo
                        CelestialBody eeloo = FlightGlobals.Bodies.Where(b => b.name == "Eeloo").First();
                        CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();
                        Vector3 perp = eeloo.transform.position - sun.transform.position;
                        float tmp = perp.x;
                        perp.x = -perp.z;
                        perp.z = tmp;
                        perp.Normalize();

                        // Set us up a nice 4 radiuses away...
                        float distance = 4.0f * (float)eeloo.Radius;
                        starJeb.SetPosition(eeloo.transform.position + perp * distance);
                        
                        // Determine which side the sun is on - makes for a better show
                        Vector3 sunnySide = sun.transform.position - eeloo.transform.position;
                        sunnySide.Normalize();

                        // Go straight at Eeloo
                        velocity = (eeloo.transform.position - starJeb.transform.position).normalized;
                        velocity *= distance / 3.0f;
                        LoggingUtil.LogVerbose(this, "kick magnitude will be: " + velocity);

                        // Now offset him down so he doesn't actually hit Eeloo...
                        starJeb.SetPosition(starJeb.transform.position + sunnySide * ((float)eeloo.Radius * 1.5f));
                        starJeb.SetWorldVelocity(eeloo.getRFrmVel(starJeb.transform.position));

                        nextState();

                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EELOO2:
                    {
                        // This time won't target directly towards Eeloo, as the player will have some idea
                        // what is up by now.
                        CelestialBody eeloo = FlightGlobals.Bodies.Where(b => b.name == "Eeloo").First();
                        CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();
                        Vector3 awayFromSun = sun.transform.position - eeloo.transform.position;
                        awayFromSun.Normalize();

                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + awayFromSun * 50.0f);

                        // Make sure that the camera gets fixed
                        if (Time.fixedTime - stepTime > 0.1f)
                        {
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EELOO3:
                    if (Time.fixedTime - stepTime > 5.5f)
                    {
                        velocity = null;

                        // Done with Eeloo
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE1:
                    {
                        CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                        Vector3 targetPosition = Destination.Value;
                        Vector3 normal = eve.GetSurfaceNVector(eveLatitude, eveLongitude);
                        startDistance = 10000000f;
                        Vector3 start = targetPosition + normal * startDistance;

                        starJeb.SetPosition(start);
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE2:
                    {
                        // Camera straight towards Eve - we're going in!
                        CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                        Vector3 awayFromEve = starJeb.transform.position - eve.transform.position;
                        awayFromEve.Normalize();

                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + awayFromEve * 15.0f);

                        // Make sure that the camera gets fixed
                        if (Time.fixedTime - stepTime > 0.1f)
                        {
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE3:
                    // Wait until we've held the position for a split second
                    if (Time.fixedTime - stepTime >= 9.3f)
                    {
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE4:
                    // Give the player a bit to get settled, then let the fun begins
                    if (Time.fixedTime - stepTime >= 15.0f)
                    {
                        // Spawn some asteroids
                        CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                        ScenarioDiscoverableObjects asteroidSpawner = (ScenarioDiscoverableObjects)HighLogic.CurrentGame.scenarios.Find(
                            s => s.moduleRef is ScenarioDiscoverableObjects).moduleRef;
                        System.Random random = new System.Random();

                        // Spawn some more asteroids
                        for (int i = 0; i < ASTEROID_COUNT; i++)
                        {
                            asteroidSpawner.SpawnAsteroid();
                        }

                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE5:
                    // Wait a full second after spawning the asteroids - we're not allowed to pull
                    // them off rails until they've been active a bit
                    if (Time.fixedTime - stepTime > 1.0f)
                    {
                        // Spawn some asteroids
                        CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                        System.Random random = new System.Random();

                        foreach (Vessel asteroid in FlightGlobals.Vessels.Where(v => v.vesselType == VesselType.SpaceObject).Reverse().Take(ASTEROID_COUNT))
                        {
                            // Set the position
                            double r = random.NextDouble() * 0.02 + 0.002;
                            double theta = random.NextDouble() * 2.0 * Math.PI;
                            double latitude = starJeb.latitude + r * Math.Sin(theta);
                            double longitude = starJeb.longitude + r * Math.Cos(theta);
                            double altitude = starJeb.altitude + 100 + random.NextDouble() * 200;
                            asteroid.SetPosition(eve.GetWorldSurfacePosition(latitude, longitude, altitude));
                            asteroid.ChangeWorldVelocity(asteroid.GetSrfVelocity());
                            asteroid.Load();
                            asteroid.GoOffRails();
                        }
                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_EVE6:
                    {
                        // Determine if there's an asteroid about to kill us
                        CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();
                        bool killerAsteroid = FlightGlobals.Vessels.Where(v => v.mainBody == eve && v.vesselType == VesselType.SpaceObject &&
                            Vector3.Distance(starJeb.transform.position, v.transform.position) < 5.5 * ((int)v.DiscoveryInfo.objectSize + 1)).Any();

                        if (killerAsteroid || Time.fixedTime - stepTime > 20.0f)
                        {
                            foreach (Vessel asteroid in FlightGlobals.Vessels.Where(v => v.vesselType == VesselType.SpaceObject).Reverse().Take(ASTEROID_COUNT))
                            {
                                asteroid.Die();
                            }
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_KERBIN1:
                    {
                        CheatOptions.NoCrashDamage = false;

                        // Start between the sun and Kerbin
                        CelestialBody kerbin = FlightGlobals.Bodies.Where(b => b.name == "Kerbin").First();
                        CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();
                        Vector3 sunnySide = sun.transform.position - kerbin.transform.position;
                        sunnySide.Normalize();

                        // Set us up a nice 4 radiuses away...
                        float distance = 4.0f * (float)kerbin.Radius;
                        starJeb.SetPosition(kerbin.transform.position + sunnySide * distance);

                        // Hardcode an orbital velocity, because it's late and I'm tired
                        starJeb.SetWorldVelocity(kerbin.getRFrmVel(starJeb.transform.position).normalized * 1085);

                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_KERBIN2:
                    {
                        // Camera to target kerbin - do this on a seperate update to allow KSP to catch up
                        CelestialBody kerbin = FlightGlobals.Bodies.Where(b => b.name == "Kerbin").First();
                        FlightCamera.fetch.setTarget(starJeb.transform);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + (starJeb.transform.position - kerbin.transform.position).normalized * 10.0f);

                        // Make sure that the camera gets fixed
                        if (Time.fixedTime - stepTime > 0.1f)
                        {
                            nextState();
                        }
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_KERBIN3:
                    if (Time.fixedTime - stepTime > 2.0f)
                    {
                        // Turn into star jeb
                        CelestialBody kerbin = FlightGlobals.Bodies.Where(b => b.name == "Kerbin").First();

                        starJeb.vesselName = "The Star Jeb";
                        Undress(starJeb.gameObject);
                        FlightCamera.fetch.SetCamCoordsFromPosition(starJeb.transform.position + (starJeb.transform.position - kerbin.transform.position).normalized * 1.5f);

                        nextState();
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_KERBIN4:
                    if (Time.fixedTime - stepTime < 15.0f)
                    {
                        CelestialBody kerbin = FlightGlobals.Bodies.Where(b => b.name == "Kerbin").First();
                        Vector3 camDirection = starJeb.transform.position + (starJeb.transform.position - kerbin.transform.position).normalized;
                    }
                    else
                    {
                        nextState();

                        monolith.Die();
                        monolith = null;

                        starJeb.Die();
                        starJeb = null;

                        Vessel discovery = ContractVesselTracker.Instance.GetAssociatedVessel("Discovery One");
                        FlightGlobals.ForceSetActiveVessel(discovery);
                    }
                    return false;
                case MonolithState.FULL_OF_STARS_FINAL:
                    MessageSystem.Instance.AddMessage(new MessageSystem.Message("The Star Jeb is Born", String.Format(STARJEB_MESSAGE, starJebName),
                        MessageSystemButton.MessageButtonColor.GREEN, MessageSystemButton.ButtonIcons.MESSAGE));
                    return true;
                default:
                    return false;
            }
        }

        private Vector3 selectEvePoint()
        {
            CelestialBody eve = FlightGlobals.Bodies.Where(b => b.name == "Eve").First();

            if (eveLatitude == 0.0)
            {
                CelestialBody sun = FlightGlobals.Bodies.Where(b => b.name == "Sun").First();

                // Three hand-picked spots
                double[][] points = new double[][] {
                    new double[] { 7.39669784381863, 3.1827231256902 },
                    new double[] { 5.90113274698536, -92.0459544666453 },
                    new double[] { 1.200368332465, 221.8829976215 }
                };

                float minDistance = float.MaxValue;
                foreach (double[] point in points)
                {
                    double latitude = point[0];
                    double longitude = point[1];

                    // Get the actual position
                    Vector3 pos = eve.GetWorldSurfacePosition(latitude, longitude, 0.0);
                    float distance = Vector3.Distance(pos, sun.transform.position);
                    if (distance < minDistance)
                    {
                        eveLatitude = point[0];
                        eveLongitude = point[1];
                        minDistance = distance;
                    }
                }
            }

            // Figure out the terrain height
            double latRads = Math.PI / 180.0 * eveLatitude;
            double lonRads = Math.PI / 180.0 * eveLongitude;
            Vector3d radialVector = new Vector3d(Math.Cos(latRads) * Math.Cos(lonRads), Math.Sin(latRads), Math.Cos(latRads) * Math.Sin(lonRads));
            double height = Math.Max(eve.pqsController.GetSurfaceHeight(radialVector) - eve.pqsController.radius, 0.0);

            return eve.GetWorldSurfacePosition(eveLatitude, eveLongitude, height + 10.0f);
        }

        protected override void OnRegister()
        {
            base.OnRegister();
            GameEvents.onCrewTransferred.Add(new EventData<GameEvents.HostedFromToAction<ProtoCrewMember, Part>>.OnEvent(OnCrewTransferred));
        }

        protected override void OnUnregister()
        {
            base.OnUnregister();
            GameEvents.onCrewTransferred.Remove(new EventData<GameEvents.HostedFromToAction<ProtoCrewMember, Part>>.OnEvent(OnCrewTransferred));
        }

        protected virtual void OnCrewTransferred(GameEvents.HostedFromToAction<ProtoCrewMember, Part> a)
        {
            // Note that the VesselType of the Kerbal coming out is set to debris initially!  This is
            // probably a bug in stock, and is unreliable in my opinion.  But we can't check that the
            // other is a vessel, as it may be a station or something else.  So we check for both
            // debris or eva, in case this behaviour changes in future.

            // Kerbal going on EVA
            if (a.to.vesselType == VesselType.EVA || a.to.vesselType == VesselType.Debris)
            {
                NewEVA(a.from.vessel, a.to.vessel);
            }

            // Kerbal coming home
            if (a.from.vesselType == VesselType.EVA || a.from.vesselType == VesselType.Debris)
            {
                ReturnEVA(a.to.vessel, a.from.vessel);
            }
        }

        protected void NewEVA(Vessel parent, Vessel eva)
        {
            if (currentState <= MonolithState.EVA && eva != null && starJeb == null)
            {
                candidate = eva;
                candidateName = eva.vesselName;

                // Force a display update
                ContractConfigurator.ContractConfigurator.OnParameterChange.Fire(Root, this);
            }
        }

        protected void ReturnEVA(Vessel parent, Vessel eva)
        {
            if (currentState <= MonolithState.EVA && eva != null && candidate == eva)
            {
                candidate = null;
                candidateName = "";
                currentState = MonolithState.STARTED;

                // Remove the approach parameter, as the name may change
                if (ParameterCount == 2)
                {
                    RemoveParameter(GetParameter(1));
                }

                // Force a display update
                ContractConfigurator.ContractConfigurator.OnParameterChange.Fire(Root, this);
            }
        }


        protected override void OnParameterSave(ConfigNode node)
        {
            node.AddValue("monolithDiscovered", monolithDiscovered);
            node.AddValue("currentState", currentState);
            if (starJeb != null)
            {
                node.AddValue("starJeb", starJeb.id);
            }
            node.AddValue("starJebName", starJebName);
            if (candidate != null)
            {
                node.AddValue("candidate", candidate.id);
            }
            node.AddValue("candidateName", candidateName);
            if (velocity != null)
            {
                node.AddValue("velocity.x", velocity.Value.x);
                node.AddValue("velocity.y", velocity.Value.y);
                node.AddValue("velocity.z", velocity.Value.z);
            }
            if (progressTreeBackup != null)
            {
                node.AddNode(progressTreeBackup);
            }
        }

        protected override void OnParameterLoad(ConfigNode node)
        {
            monolithDiscovered = ConfigNodeUtil.ParseValue<bool>(node, "monolithDiscovered");
            currentState = ConfigNodeUtil.ParseValue<MonolithState>(node, "currentState");
            starJeb = ConfigNodeUtil.ParseValue<Vessel>(node, "starJeb", (Vessel)null);
            candidate = ConfigNodeUtil.ParseValue<Vessel>(node, "candidate", starJeb);
            starJebName = ConfigNodeUtil.ParseValue<string>(node, "starJebName", "");
            candidateName = ConfigNodeUtil.ParseValue<string>(node, "candidateName", "");
            if (node.HasValue("velocity.x"))
            {
                float x = ConfigNodeUtil.ParseValue<float>(node, "velocity.x");
                float y = ConfigNodeUtil.ParseValue<float>(node, "velocity.y");
                float z = ConfigNodeUtil.ParseValue<float>(node, "velocity.z");
                velocity = new Vector3(x, y, z);
            }
            if (node.HasNode("PROGRESS_TREE_BACKUP"))
            {
                progressTreeBackup = node.GetNode("PROGRESS_TREE_BACKUP");
            }

            stepTime = Time.fixedTime;

            ParameterDelegate<string>.OnDelegateContainerLoad(node);
            CreateDelegates();
        }

        protected override void OnUpdate()
        {
 	        base.OnUpdate();

            if (monolith == null && (HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION))
            {
                monolith = ContractVesselTracker.Instance.GetAssociatedVessel("Monolith");
                if (monolith != null)
                {
                    monolith.vesselRanges.orbit.load = MONOLITH_DRAW_DISTANCE * 1.1f;
                    monolith.vesselRanges.orbit.unload = MONOLITH_DRAW_DISTANCE * 1.05f;

                    // Set monolith name to unknown
                    if (!monolithDiscovered && monolith.vesselName != "???")
                    {
                        monolith.vesselName = "???";
                        GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(monolith, "Monolith", "???"));
                    }
                }
            }

            if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null)
            {
                // Set the load distance for the monolith to be much further
                SetLoadDistance();

                // Check our script progress
                if (ParameterDelegate<MonolithParameter>.CheckChildConditions(this, this))
                {
                    nextState();
                    ContractConfigurator.ContractConfigurator.OnParameterChange.Fire(Root, this);
                }

                if (ChildChanged)
                {
                    ContractConfigurator.ContractConfigurator.OnParameterChange.Fire(Root, this);
                    ChildChanged = false;
                }

                if (currentState == MonolithState.FINISHED)
                {
                    // Reset the state of the progress tree
                    ProgressTracking.Instance.OnLoad(progressTreeBackup);

                    // Complete the parameter
                    SetState(ParameterState.Complete);
                }
            }
        }

        private void nextState()
        {
            stepTime = Time.fixedTime;
            currentState++;
            LoggingUtil.LogVerbose(this, "Moved to state: " + currentState);
            CreateDelegates();
        }

        private void SetLoadDistance()
        {
            // Check the distance
            if (monolith != null)
            {
                distance = Vector3.Distance(FlightGlobals.ActiveVessel.transform.position, monolith.transform.position);
                if (!monolithDiscovered && distance < MONOLITH_DISCOVERY_DISTANCE)
                {
                    monolithDiscovered = true;
                    monolith.vesselName = "Monolith";
                    GameEvents.onVesselRename.Fire(new GameEvents.HostedFromToAction<Vessel, string>(monolith, "???", "Monolith"));
                }
            }
        }

        // From blizzy's toolbar easter egg
        private void Undress(GameObject kerbal)
        {
            foreach (Renderer renderer in kerbal.GetComponentsInChildren<Renderer>()
                .Where(r => (r.name == "helmet") || (r.name == "visor") || r.name.StartsWith("jetpack_base") ||
                    r.name.Contains("handle") || r.name.Contains("thruster") || r.name.Contains("tank") ||
                    r.name.Contains("pivot") || r.name.EndsWith("_a01") || r.name.EndsWith("_b01")))
            {

                renderer.enabled = false;
            }
        }
    }
}
