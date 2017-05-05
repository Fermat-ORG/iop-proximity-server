﻿using ProximityServer.Data.Models;
using Iop.Proximityserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using ProximityServer.Network;
using IopCommon;
using IopServerCore.Data;

namespace ProximityServer.Data.Repositories
{
  /// <summary>
  /// Generic repository for activities, which is the base for PrimaryActivityReposity for primary identities of this proximity server
  /// and NeighborActivityRepository for identities managed by this proximity server's neighbors.
  /// </summary>
  public class ActivityRepository<T> : GenericRepository<T> where T : ActivityBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProximityServer.Data.Repositories.ActivityRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public ActivityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }

    /// <summary>
    /// Retrieves list of activities from database that match specific criteria.
    /// </summary>
    /// <param name="ResultOffset">Zero-based index of the first result to retrieve.</param>
    /// <param name="MaxResults">Maximum number of results to retrieve.</param>
    /// <param name="ActivityIdFilter">Activity ID or 0 if activity ID is not known. If non-zero, <paramref name="OwnerIdFilter"/> must not be null.</param>
    /// <param name="OwnerIdFilter">Network identifier of the identity that created matching activities, or null if filtering by the owner is not required.</param>
    /// <param name="TypeFilter">Wildcard filter for activity type, or empty string if activity type filtering is not required.</param>
    /// <param name="StartNotAfterFilter">Maximal start time of activity, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBeforeFilter">Minimal expiration time of activity, or null if expiration time filtering is not required.</param>
    /// <param name="LocationFilter">If not null, this value together with <paramref name="RadiusFilter"/> provide specification of target area, in which the identities has to have their location set. If null, GPS location filtering is not required.</param>
    /// <param name="RadiusFilter">If <paramref name="LocationFilter"/> is not null, this is the target area radius with the centre in <paramref name="LocationFilter"/>.</param>
    /// <returns>List of activities that match the specific criteria.</returns>
    /// <remarks>On this level we query the database with an unprecise set of filters. The location filter uses GPS square instead of cirle target area 
    /// and there is no extraData filter. This means the output of this function is a superset of what we are looking for and the caller is responsible 
    /// to filter the output to get the exact set.</remarks>
    public async Task<List<T>> ActivitySearchAsync(uint ResultOffset, uint MaxResults, uint ActivityIdFilter, byte[] OwnerIdFilter, string TypeFilter, DateTime? StartNotAfterFilter, DateTime? ExpirationNotBeforeFilter, GpsLocation LocationFilter, uint RadiusFilter)
    {
      log.Trace("(ResultOffset:{0},MaxResults:{1},ActivityIdFilter:{2},OwnerIdFilter:'{3}',TypeFilter:'{4}',StartNotAfterFilter:{5},ExpirationNotBeforeFilter:{6},LocationFilter:[{7}],RadiusFilter:{8})",
        ResultOffset, MaxResults, ActivityIdFilter, OwnerIdFilter != null ? OwnerIdFilter.ToHex() : "", TypeFilter, StartNotAfterFilter != null ? StartNotAfterFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null",
        ExpirationNotBeforeFilter != null ? ExpirationNotBeforeFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", LocationFilter, RadiusFilter);

      // First we obtain result candidates from the database. These candidates may later be filtered out by more precise use of location filter 
      // and application of the extraData filter. As we want to achieve a certain number of results, we will be working in a loop,
      // in which we query a certain number of records, then possibly filter out some of them with more precise location filter and extraData filter.
      // Then if we do not have enough results, we load more.

      // This is to exclude special internal activity type.
      IQueryable<T> query = dbSet.Where(a => a.Type != ActivityBase.InternalInvalidActivityType);

      // Apply activity ID filter if any.
      if (ActivityIdFilter != 0) query = query.Where(a => a.ActivityId == ActivityIdFilter);

      // Apply owner identity ID filter if any.
      if (OwnerIdFilter != null) query = query.Where(a => a.OwnerIdentityId == OwnerIdFilter);

      // Apply type filter if any.
      if (!string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**"))
      {
        Expression<Func<T, bool>> typeFilterExpression = GetTypeFilterExpression<T>(TypeFilter);
        query = query.Where(typeFilterExpression);
      }

      // Apply start not after filter if any.
      if (StartNotAfterFilter != null) query = query.Where(a => a.StartTime <= StartNotAfterFilter.Value);

      // Apply expiration not before filter if any.
      if (ExpirationNotBeforeFilter != null) query = query.Where(a => a.ExpirationTime >= ExpirationNotBeforeFilter.Value);


      // Apply basic location filter if any.
      // We do not make a precise computation of whether the activity is within the target area in DB query.
      // We only present certain limits within latitude and longitude values must be and from the results we get 
      // we then filter those that are out of the target area.
      if (LocationFilter != null)
      {
        Expression<Func<T, bool>> locationFilterExpression = GetLocationFilterExpression<T>(LocationFilter, RadiusFilter);
        if (locationFilterExpression != null)
          query = query.Where(locationFilterExpression);
      }

      // Limit size of the result.
      if (ResultOffset > 0) query = query.Skip((int)ResultOffset).Take((int)MaxResults);
      else query = query.Take((int)MaxResults);

      // Execute query with well defined ordering.
      List<T> res = await query.OrderBy(a => a.OwnerIdentityId).ThenBy(a => a.ActivityId).ToListAsync();

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates filter expression for type.
    /// </summary>
    /// <param name="WildcardFilter">Type filter.</param>
    /// <returns>Filter expression for the database query.</returns>
    public static Expression<Func<Q, bool>> GetTypeFilterExpression<Q>(string WildcardFilter) where Q : ActivityBase
    {
      log.Trace("(WildcardFilter:'{0}')", WildcardFilter);
      string wildcardFilter = WildcardFilter.ToLowerInvariant();
      Expression<Func<Q, bool>> res = a => a.Type.ToLower() == wildcardFilter;

      // Example: WildcardFilter = "*abc"
      // This means that when filter STARTS with '*', we want the property value to END with "abc".
      // Note that WildcardFilter == "*" case is handled elsewhere.
      bool valueStartsWith = wildcardFilter.EndsWith("*");
      bool valueEndsWith = wildcardFilter.StartsWith("*");
      bool valueContains = valueStartsWith && valueEndsWith;

      if (valueContains)
      {
        wildcardFilter = wildcardFilter.Substring(1, wildcardFilter.Length - 2);
        res = a => a.Type.ToLower().Contains(wildcardFilter);
      }
      else if (valueStartsWith)
      {
        wildcardFilter = wildcardFilter.Substring(0, wildcardFilter.Length - 1);
        res = a => a.Type.ToLower().StartsWith(wildcardFilter);
      }
      else if (valueEndsWith)
      {
        wildcardFilter = wildcardFilter.Substring(1);
        res = a => a.Type.ToLower().EndsWith(wildcardFilter);
      }

      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Creates basic filter expression for location. This filter is not precise filter, 
    /// it will just filter out the majority of the activities that the caller is not interested it.
    /// </summary>
    /// <param name="LocationFilter">GPS location of the target area centre.</param>
    /// <param name="Radius">Target area radius in metres.</param>
    /// <returns>Filter expression for the database query.</returns>
    public static Expression<Func<Q, bool>> GetLocationFilterExpression<Q>(GpsLocation LocationFilter, uint Radius) where Q : ActivityBase
    {
      log.Trace("(LocationFilter:'{0}',Radius:{1})", LocationFilter, Radius);
      Expression<Func<Q, bool>> res = null;

      // There are several separated cases:
      //  1) Radius is very large - i.e. greater than 5,000 km. In this case, we do no filtering on this level at all.
      //  2) Distance of the target area centre to one of the poles is not larger than the radius. In this case, we calculate latitude and longitude ranges,
      //     from which we then construct a target rectangle on the sphere that will represent our target area of interest.
      //  3) Distance of the target area centre to one of the poles is larger than the radius. In this case, we only set some of the boundaries,
      //     but we will not have a full rectangle on the sphere. There are several subcases here described below.


      // 1) Radius is very large, no filtering.
      if (Radius > 5000000)
      {
        log.Trace("(-)[LARGE_RADIUS]");
        return res;
      }

      GpsLocation northPole = new GpsLocation(90.0m, 0.0m);
      GpsLocation southPole = new GpsLocation(-90.0m, 0.0m);

      double northPoleDistance = LocationFilter.DistanceTo(northPole);
      double southPoleDistance = LocationFilter.DistanceTo(southPole);

      // We have to add maximum allowed location precision to the radius here to include all activities 
      // that could possibly be within the range. Actual activity precision will be used later 
      // to filter out activities precisely with the location filter.
      double radius = (double)Radius + (double)ActivityBase.MaxLocationPrecision;
      if (radius >= northPoleDistance)
      {
        // 2) Distance to pole is not larger than the radius:
        //    a) North Pole
        //
        // In this case we go to the South from the centre to find the minimal latitude
        // and there will be no limit on longitude.
        GpsLocation minLatitudeLocation = LocationFilter.GoVector(GpsLocation.BearingSouth, radius);
        log.Trace("Radius >= North Pole Distance, min latitude is {0}.", minLatitudeLocation.Latitude);
        res = a => a.LocationLatitude >= minLatitudeLocation.Latitude;
      }
      else if (radius >= southPoleDistance)
      {
        // 2) Distance to pole is not larger than the radius:
        //    b) South Pole
        //
        // In this case we go to the North from the centre to find the maximal latitude.
        // and there will be no limit on longitude.
        GpsLocation maxLatitudeLocation = LocationFilter.GoVector(GpsLocation.BearingNorth, radius);
        log.Trace("Radius >= South Pole Distance, max latitude is {0}.", maxLatitudeLocation.Latitude);
        res = a => a.LocationLatitude <= maxLatitudeLocation.Latitude;
      }
      else
      {
        // 3) Distance to poles is larger than the radius.
        // 
        // In this case we create a rectangle on the sphere, in which the target identities are expected to be.
        // Using this square we will find latitude and longitude ranges for the database query.

        // Find a GPS square that contains the whole target circle area.
        GpsSquare square = LocationFilter.GetSquare((double)Radius);

        // Get latitude range - this is simple, left-top and right-top corners define the max latitude,
        // and left-bottom and right-bottom corners define the min latitude.
        decimal maxLatitude = square.MidTop.Latitude;
        decimal minLatitude = square.MidBottom.Latitude;
        log.Trace("GPS square is {0}, min latitude is {1}, max latitude is {2}.", square, minLatitude, maxLatitude);

        // Get longitude range - we have to examine all four corners here as it depends on which hemisphere they are
        // and there are several different cases due to possibility of crossing longitude 180.

        bool leftCornersSameSign = Math.Sign(square.LeftBottom.Longitude) == Math.Sign(square.LeftTop.Longitude);
        bool rightCornersSameSign = Math.Sign(square.RightBottom.Longitude) == Math.Sign(square.RightTop.Longitude);

        if (leftCornersSameSign && rightCornersSameSign && (Math.Sign(square.LeftTop.Longitude) == Math.Sign(square.RightTop.Longitude)))
        {
          // a) Square does not cross longitude 180. This case is simple, we find left most and right most longitudes 
          // and our target activity has to be between those two.
          decimal leftLongitude = Math.Min(square.LeftTop.Longitude, square.LeftBottom.Longitude);
          decimal rightLongitude = Math.Max(square.RightTop.Longitude, square.RightBottom.Longitude);

          log.Trace("Square does not cross lon 180. left longitude is {0}, right longitude is {1}.", leftLongitude, rightLongitude);
          res = a => (minLatitude <= a.LocationLatitude) && (a.LocationLatitude <= maxLatitude)
            && (leftLongitude <= a.LocationLongitude) && (a.LocationLongitude <= rightLongitude);
        }
        else
        {
          decimal leftLongitude;
          decimal rightLongitude;

          // b) Square crosses longitude 180. This is the more complicated case. One or two corners 
          // have positive longitude and the remaining have negative longitude.
          if (leftCornersSameSign)
          {
            // Left top and left bottom corners are on the same side of longitude 180.
            // The left most corner is the one with smaller longitude value.
            leftLongitude = Math.Min(square.LeftTop.Longitude, square.LeftBottom.Longitude);
          }
          else
          {
            // Left top and left bottom corners are NOT on the same side of longitude 180.
            // The left most corner is the one with the positive value as the negative value is on the right of longitude 180.
            leftLongitude = square.LeftTop.Longitude > 0 ? square.LeftTop.Longitude : square.LeftBottom.Longitude;
          }

          if (rightCornersSameSign)
          {
            // Right top and right bottom corners are on the same side of longitude 180.
            // The right most corner is the one with higher longitude value.
            rightLongitude = Math.Max(square.RightTop.Longitude, square.RightBottom.Longitude);
          }
          else
          {
            // Right top and right bottom corners are NOT on the same side of longitude 180.
            // The right most corner is the one with the negative value as the positive value is on the left of longitude 180.
            rightLongitude = square.RightTop.Longitude < 0 ? square.RightTop.Longitude : square.RightBottom.Longitude;
          }

          // Note the OR operator instead of AND operator for longitude comparison.
          // This is because a longitude value can not be higher than e.g. 170 and lower than e.g. -150 at the same time.
          // The point is within the square if its longitude is 170 or more (up to 180) OR -150 or less (down to -180).
          log.Trace("Square crosses lon 180. left longitude is {0}, right longitude is {1}.", leftLongitude, rightLongitude);
          res = a => (minLatitude <= a.LocationLatitude) && (a.LocationLatitude <= maxLatitude)
            && ((leftLongitude <= a.LocationLongitude) || (a.LocationLongitude <= rightLongitude));
        }
      }

      log.Trace("(-)");
      return res;
    }
  }
}
