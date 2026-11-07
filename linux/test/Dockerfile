FROM debian:trixie-slim
RUN apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends \
    xvfb x11-utils x11-apps imagemagick dbus dbus-x11 libglib2.0-bin python3-gi gir1.2-glib-2.0 \
    libfontconfig1 libice6 libsm6 libx11-6 libxrandr2 libxi6 libxcursor1 libxext6 libxrender1 \
    fonts-dejavu-core nodejs procps xdotool openbox ca-certificates xterm
