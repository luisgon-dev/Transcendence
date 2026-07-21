/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",
  images: {
    qualities: [55, 75],
    remotePatterns: [
      {
        protocol: "https",
        hostname: "ddragon.leagueoflegends.com",
        pathname: "/cdn/**"
      },
      {
        protocol: "https",
        hostname: "ddragon.leagueoflegends.com",
        pathname: "/cdn/img/**"
      },
      {
        protocol: "https",
        hostname: "raw.communitydragon.org",
        pathname: "/latest/plugins/rcp-be-lol-game-data/global/default/**"
      },
      {
        protocol: "https",
        hostname: "raw.communitydragon.org",
        pathname: "/latest/plugins/rcp-fe-lol-static-assets/global/default/**"
      },
      {
        protocol: "https",
        hostname: "raw.communitydragon.org",
        pathname: "/latest/game/assets/**"
      }
    ]
  },
  async redirects() {
    // The standalone Matchups surface was folded into the champion pages.
    return [
      {
        source: "/lol/matchups",
        destination: "/lol/champions",
        permanent: true
      },
      {
        source: "/lol/matchups/:championId",
        destination: "/lol/champions/:championId",
        permanent: true
      }
    ];
  },
  experimental: {
    optimizePackageImports: ["radix-ui", "framer-motion"]
  }
};

export default nextConfig;
