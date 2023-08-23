###############
# Build Stage #
###############

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/nightly/sdk:8.0-preview AS build
RUN echo 'Acquire::Retries "5";Acquire::https::Timeout "30";Acquire::http::Timeout "30";APT::Get::Assume-Yes "true";' > /etc/apt/apt.conf.d/99custom
RUN apt update
RUN apt install build-essential 
RUN apt install debhelper 
RUN apt install dh-make
RUN apt install binutils-aarch64-linux-gnu

COPY . /homecast
RUN find homecast -maxdepth 1 ! -path 'homecast' ! -name 'HomeCast' ! -name 'HomeHook.Common' ! -name 'YoutubeDLSharp' -exec rm -rv {} +

ARG LOGNAME=root
ARG USER=root
ARG VERSION
ARG DEBEMAIL
ARG DEBFULLNAME
ARG TARGETARCH
ARG CONFIG

RUN mv homecast homecast-$VERSION
RUN tar cvzf homecast_$VERSION.tar.gz homecast-$VERSION
RUN cd /homecast-$VERSION && dh_make -y -f ../homecast_$VERSION.tar.gz -s -c gpl3 -n
RUN cd /homecast-$VERSION/debian && rm *ex && rm README README.Debian README.source homecast-docs.docs
RUN cp /homecast-$VERSION/HomeCast/debian/* /homecast-$VERSION/debian
RUN sed -i "s/@@arch/$TARGETARCH/g" /homecast-$VERSION/debian/rules
RUN sed -i "s/@@arch/$TARGETARCH/g" /homecast-$VERSION/debian/control

RUN curl -LJO https://raw.githubusercontent.com/MattMckenzy/MattApt/master/add-source.sh && chmod 0775 add-source.sh && ./add-source.sh
RUN apt update \
	&& apt source homecast \
	&& cd homecast-$(apt show homecast | grep "Version:" | awk -F'[ -]' '{print $2}') \
	&& uupdate -v $VERSION ../homecast_$VERSION.tar.gz \
	&& cd ../homecast-$VERSION \
	&& while dquilt push; do dquilt refresh; done \
	|| echo "No prior package."

RUN mkdir /package
RUN cd /homecast-$VERSION && dpkg-source -b .
RUN cp /homecast_${VERSION}.tar.xz /package
RUN cp /homecast_${VERSION}.dsc /package
RUN cd /homecast-$VERSION && dpkg-buildpackage --host-arch=$TARGETARCH -b --no-sign
RUN cp /homecast_${VERSION}_${TARGETARCH}.* /package


#################
# Publish Stage #
#################

FROM debian:bookworm-slim AS publish
RUN echo 'Acquire::Retries "5";Acquire::https::Timeout "30";Acquire::http::Timeout "30";APT::Get::Assume-Yes "true";' > /etc/apt/apt.conf.d/99custom
RUN apt update
RUN apt install git
RUN apt install gh
RUN apt install reprepro
RUN apt install gnupg2

COPY --from=build /package /package
RUN git clone https://github.com/MattMckenzy/MattApt.git
WORKDIR MattApt

ARG GITHUB_TOKEN
ARG APT_SECRET_KEY
ARG APT_SECRET_KEY_FINGERPRINT
ARG DEBEMAIL
ARG DEBFULLNAME

RUN echo "$APT_SECRET_KEY" | base64 -d | gpg2 --import -
RUN echo "$APT_SECRET_KEY_FINGERPRINT:6:" | gpg2 --import-ownertrust
RUN gpg-connect-agent /bye \
	&& reprepro --basedir . includedeb bookworm /package/homecast*.deb
RUN reprepro --basedir . list bookworm

RUN git config --global user.email "${DEBEMAIL}"
RUN git config --global user.name "${DEBFULLNAME}"
RUN echo "https://${DEBFULLNAME}:${GITHUB_TOKEN}@github.com" > ~/.git-credentials
RUN git config credential.helper store

RUN git add --all
RUN git commit -m "Added package: $(basename /package/homecast*.deb)"
RUN git pull
RUN git push origin main


#################
# Package Stage #
#################

FROM scratch AS package
COPY --from=publish /package /package