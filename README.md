# Real-Debrid Torrent Client (Test/)

This is a web interface test to manage your torrents on Real-Debrid, AllDebrid or Premiumize. It supports the following features:

- Add new torrents through magnets or files
- Download all files from Real-Debrid, AllDebrid or Premiumize to your local machine automatically
- Unpack all files when finished downloading
- Implements a fake qBittorrent API so you can hook up other applications like Sonarr, Radarr or Couchpotato.
- Built with Angular 15 and .NET 8

**You will need a Premium service at Real-Debrid, AllDebrid or Premiumize!**

[Click here to sign up for Real-Debrid.](https://real-debrid.com/?id=1348683)

[Click here to sign up for AllDebrid.](https://alldebrid.com/?uid=2v91l)

[Click here to sign up for Premiumize.](https://www.premiumize.me/)

<sub>(referal links so I can get a few free premium days)</sub>

## Docker Setup

You can run the docker container on Windows, Linux. To get started either use _Docker Compose_.

### Docker Compose

You can use the provided docker compose to run:

```yaml
version: "3"
services:
  rdtclient:
    restart: unless-stopped
    container_name: rdtclient
    # build:
    #     context: .
    #     dockerfile: Dockerfile
    image: mentalblank/rdtclient:latest
    hostname: rdtclient
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=ETC/UTC
    logging:
        driver: json-file
        options:
            max-size: 10m
    ports:
      - 6500/tcp
    networks:
      - saltbox
    labels:
      com.github.saltbox.saltbox_managed: true 
      traefik.enable: true 
      traefik.http.routers.rdtclient-http.entrypoints: web 
      traefik.http.routers.rdtclient-http.middlewares: globalHeaders@file,redirect-to-https@docker,cloudflarewarp@docker 
      traefik.http.routers.rdtclient-http.rule: Host(`rdtclient.yourdomain.com`) 
      traefik.http.routers.rdtclient-http.service: rdtclient 
      traefik.http.routers.rdtclient.entrypoints: websecure 
      traefik.http.routers.rdtclient.middlewares: globalHeaders@file,secureHeaders@file,cloudflarewarp@docker 
      traefik.http.routers.rdtclient.rule: Host(`rdtclient.yourdomain.com`) 
      traefik.http.routers.rdtclient.service: rdtclient 
      traefik.http.routers.rdtclient.tls.certresolver: cfdns 
      traefik.http.routers.rdtclient.tls.options: securetls@file 
      traefik.http.services.rdtclient.loadbalancer.server.port: 6500 
    volumes:
      - /opt/rdtclient:/CONFIG
      - /etc/localtime:/etc/localtime:ro
      - /mnt:/mnt
      - /opt/rdtclient/data:/data
      - /opt/rdtclient/data/db:/data/db

networks:
  saltbox:
    external: true
```

And to run:

```console
docker-compose up -d
```

Replace the paths in `volumes` as in the above step.

## Setup

### First Login

1. Browse to [http://127.0.0.1:6500](http://127.0.0.1:6500) (or the path of your server).
1. The very first credentials you enter in will be remembered for future logins.
1. Click on `Settings` on the top and enter your Real-Debrid API key (found here: [https://real-debrid.com/apitoken](https://real-debrid.com/apitoken).
1. If you are using docker then the `Download path` setting needs to be the same as in your docker file mapping. By default this is `/data/downloads`. If you are using Windows, this is a path on your host.
1. Same goes for `Mapped path`, but this is the destination path from your docker mapping. This is a path on your host. For Windows, this will most likely be the same as the `Download path`.
1. Save your settings.

### Download Clients

Currently there 4 available download clients:

#### Internal Downloader

This experimental [downloader](https://github.com/mentalblank/Downloader.NET) can be used to download files with multiple sections in parallel.

It has the following options:

- Download speed (in MB/s): This number indicates the speed in MB/s per download over all parallel downloads and chunks.
- Parallel connections per download: When a file is downloaded it is split in sections, this setting indicates how many sections you will download in parallel.
- Connection Timeout: This number indicates the timeout in milliseconds before a download chunk times out. It will retry each chunk 5 times before completely failing.

#### Bezzad Downloader

This [downloader](https://github.com/bezzad/Downloader) can be used to download files in parallel and with multiple chunks.

It has the following options:

- Download speed (in MB/s): This number indicates the speed in MB/s per download over all parallel downloads and chunks.
- Parallel connections per download: This number indicates how many parallel it will use per download. This can increase speed, recommended is no more than 8.
- Parallel chunks per download: This number indicates in how many chunks each download is split, recommended is no more than 8.
- Connection Timeout: This number indicates the timeout in milliseconds before a download chunk times out. It will retry each chunk 5 times before completely failing.

#### Aria2c downloader

This will use an external Aria2c downloader client. You will need to install this client yourself on your host, it is not included in the docker image.

It has the following options:

- Url: The full URL to your Aria2c service. This must end in /jsonrpc. A standard path is `http://192.168.10.2:6800/jsonrpc`.
- Secret: Optional secret to connecto to your Aria2c service.

If Aria2c is selected, none of the above options for `Internal Downloader` are used, you have to configure Aria2c manually.

#### Symlink downloader

Symlink downloader requires a rclone mount to be mounted into your filesystem. Be sure to keep the exact path to mounted files in other apps exactly
the same as used by rdt-client. Otherwise the symlinks wont resolve the file its trying to point to.

If the mount path folder cant be found the client wont start downloading anything.

Required configuration:
- Post Download Action = DO NOT SELECT REMOVE FROM PROVIDER
- Rclone mount path = /PATH_TO_YOUR_RCLONE_MOUNT/torrents/

Suggested configuration:
- Automatic retry downloads > 3

### Troubleshooting

- If you forgot your logins simply delete the `rdtclient.db` and restart the service.
- A log file is written to your persistent path as `rdtclient.log`. When you run into issues please change the loglevel in your docker script to `Debug`.

### Connecting Sonarr/Radarr

RdtClient emulates the qBittorrent web protocol and allow applications to use those APIs. This way you can use Sonarr and Radarr to download directly from RealDebrid.

1. Login to Sonarr or Radarr and click `Settings`.
1. Go to the `Download Client` tab and click the plus to add.
1. Click `qBittorrent` in the list.
1. Enter the IP or hostname of the RealDebridClient in the `Host` field.
1. Enter the 6500 in the `Port` field.
1. Enter your Username/Password you setup above in the Username/Password field.
1. Set the category to `sonarr` for Sonarr or `radarr` for Radarr.
1. Leave the other settings as is.
1. Hit `Test` and then `Save` if all is well.
1. Sonarr will now think you have a regular Torrent client hooked up.

When downloading files it will append the `category` setting in the Sonarr/Radarr Download Client setting. For example if your Remote Path setting is set to `C:\Downloads` and your Sonarr Download Client setting `category` is set to `sonarr` files will be downloaded to `C:\Downloads\sonarr`.

Notice: the progress and ETA reported in Sonarr's Activity tab will not be accurate, but it will report the torrent as completed so it can be processed after it is done downloading.

### Running within a folder

By default the application runs in the root of your hosted address (i.e. https://rdt.myserver.com/), but if you want to run it as a relative folder (i.e. https://myserver.com/rdt) you will have to change the `BasePath` setting in the `appsettings.json` file. You can set the `BASE_PATH` environment variable for docker enviroments.

## Build instructions

### Prerequisites

- NodeJS
- NPM
- Angular CLI
- .NET 8
- Visual Studio 2022
- (optional) Resharper

1. Open the client folder project in VS Code and run `npm install`.
1. To debug run `ng serve`, to build run `ng build --prod`.
1. Open the Visual Studio 2019 project `RdtClient.sln` and `Publish` the `RdtClient.Web` to the given `PublishFolder` target.
1. When debugging, make sure to run `RdtClient.Web.dll` and not `IISExpress`.
1. The result is found in `Publish`.

## Build docker container

1. In the root of the project run `docker build --tag rdtclient .`
1. To create the docker container run `docker run --publish 6500:6500 --detach --name rdtclientdev rdtclient:latest`
1. To stop: `docker stop rdtclient`
1. To remove: `docker rm rdtclient`
1. Or use `docker-build.bat`
