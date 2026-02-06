/** @type {import('next').NextConfig} */
const nextConfig = {
  transpilePackages: ["@transcendence/api-client"],
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "ddragon.leagueoflegends.com",
        pathname: "/cdn/**"
      }
    ]
  }
};

export default nextConfig;

